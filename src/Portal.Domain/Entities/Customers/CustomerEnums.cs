namespace Portal.Domain.Entities.Customers;

public enum CustomerType { Individual, Company }

public enum ContactChannel { Email, Phone, WhatsApp, Sms }

public enum PreferredLanguage { English, Arabic }

public enum InteractionType { Call, Email, Meeting, WhatsApp, Sms, Chat, Other }

public enum InteractionDirection { Inbound, Outbound }
