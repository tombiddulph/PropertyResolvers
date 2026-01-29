using System;

namespace SampleProject;

// This code will not compile until you build the project with the Source Generators

public class Examples
{
    public static void Demo()
    {
        var order = new Order { AccountId = "ACC-123", TenantId = "TENANT-1" };
        var customer = new Customer { AccountId = "ACC-456" };
        var invoice = new Invoice { AccountId = "ACC-789", TenantId = "TENANT-2" };
        var product = new Product { Name = "Widget" };
        var entity = new TestEntity { EntityId = Guid.NewGuid() };


        // These work - types have the property
        var accountId1 = AccountIdResolver.GetAccountId(order); // ACC-123
        var accountId2 = AccountIdResolver.GetAccountId(customer); // ACC-456
        var accountId3 = AccountIdResolver.GetAccountId(invoice); // ACC-789

        // This returns null - Product doesn'SampleProject.Generated.t have AccountId
        var accountId4 = AccountIdResolver.GetAccountId(product); // null

        // TenantId resolver
        var tenantId1 = TenantIdResolver.GetTenantId(order); // TENANT-1
        var tenantId2 = TenantIdResolver.GetTenantId(invoice); // TENANT-2
        var tenantId3 = TenantIdResolver.GetTenantId(customer); // null (Customer doesn't have TenantId)
        
        var entityId = EntityIdResolver.GetEntityId(entity);

        Console.WriteLine(accountId1);
        Console.WriteLine(accountId2);
        Console.WriteLine(accountId3);
        Console.WriteLine(accountId4 ?? "null");    
        Console.WriteLine(tenantId1);
        Console.WriteLine(tenantId2);
        Console.WriteLine(tenantId3 ?? "null");
        Console.WriteLine(entityId);
    }
}