using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Portal.Application.Common.Security;
using Portal.Application.Features.Roles;
using Portal.Application.Features.Users;
using Portal.Domain.Authorization;

namespace Portal.Tests.Unit;

public sealed class PermissionRegistryTests
{
    [Fact]
    public void Keys_are_unique()
    {
        PermissionRegistry.All.Select(p => p.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_permission_constant_is_registered()
    {
        // Guards against adding a constant to Permissions and forgetting to register it (it would never be grantable).
        var constants = typeof(Permissions).GetNestedTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);

        PermissionRegistry.All.Select(p => p.Key).Should().BeEquivalentTo(constants);
    }

    [Fact]
    public void Keys_follow_module_dot_action_convention()
    {
        PermissionRegistry.All.Should().OnlyContain(p => p.Key.StartsWith(p.Module + "."));
    }
}

public sealed class ValidatorTests
{
    private static CreateUserRequest ValidUser() =>
        new("Sara", "Ali", "sara@test.local", "+966 55 123 4567", "Passw0rd!", Guid.NewGuid());

    [Fact]
    public void Valid_create_user_request_passes()
    {
        new CreateUserRequestValidator().Validate(ValidUser()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12")]
    [InlineData("+966-55-123-4567-8888-9999")]
    public void Invalid_phone_numbers_fail(string phone)
    {
        var result = new CreateUserRequestValidator().Validate(ValidUser() with { PhoneNumber = phone });
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateUserRequest.PhoneNumber));
    }

    [Fact]
    public void Phone_number_is_optional()
    {
        new CreateUserRequestValidator().Validate(ValidUser() with { PhoneNumber = " " }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bad<script>")]
    public void Invalid_role_names_fail(string name)
    {
        new CreateRoleRequestValidator().Validate(new CreateRoleRequest(name, null, null)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Setting_permissions_requires_at_least_one_id()
    {
        new SetRolePermissionsRequestValidator().Validate(new SetRolePermissionsRequest([], true)).IsValid.Should().BeFalse();
    }
}

public sealed class PermissionCacheTests
{
    [Fact]
    public async Task Returns_cached_value_until_invalidated()
    {
        var cache = new PermissionCache(new MemoryCache(new MemoryCacheOptions()));
        var userId = Guid.NewGuid();
        var calls = 0;
        Task<IReadOnlySet<string>> Load() { calls++; return Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { $"v{calls}" }); }

        (await cache.GetOrAddAsync(userId, Load)).Should().Contain("v1");
        (await cache.GetOrAddAsync(userId, Load)).Should().Contain("v1");
        calls.Should().Be(1);

        cache.InvalidateAll();

        (await cache.GetOrAddAsync(userId, Load)).Should().Contain("v2");
        calls.Should().Be(2);
    }
}

public sealed class PagedQueryTests
{
    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 500, 1, 100)]
    [InlineData(3, 25, 3, 25)]
    public void Page_and_page_size_are_clamped(int page, int size, int expectedPage, int expectedSize)
    {
        var q = new UserListQuery { Page = page, PageSize = size };
        q.Page.Should().Be(expectedPage);
        q.PageSize.Should().Be(expectedSize);
    }
}
