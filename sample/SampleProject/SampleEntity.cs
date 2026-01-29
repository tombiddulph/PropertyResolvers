using System;
using PropertyResolvers.Attributes;

// Configure what properties we want resolvers for
[assembly: GeneratePropertyResolver("AccountId", ExcludeNamespaces = ["System", "Microsoft"])]
[assembly: GeneratePropertyResolver("TenantId", ExcludeNamespaces = ["System", "Microsoft"])]
[assembly: GeneratePropertyResolver("EntityId", ExcludeNamespaces = ["System", "Microsoft"])]
namespace SampleProject;


public class TestEntity
{
    public Guid EntityId { get; set; }
}

public class TestEntity2
{
    // ReSharper disable once InconsistentNaming
    public int entityId { get; set; }
}

public class Order
{
    public string AccountId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public decimal Amount { get; set; }
}

public class Customer
{
    public string AccountId { get; set; } = "";
    public string Name { get; set; } = "";
}

public class Invoice
{
    public string AccountId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public int InvoiceNumber { get; set; }
}

// This one doesn't have AccountId - should be excluded
public class Product
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
