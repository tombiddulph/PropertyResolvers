using System;
using PropertyResolvers.Attributes;

[assembly: GeneratePropertyResolver("Customer.AggregateId",
    Aliases = new[] { "Legacy.AggregateId" },
    Namespace = "SampleProject.Generated",
    ResolverName = "TransferAccountResolver",
    Output = ResolverOutput.Typed,
    IncludeNamespaces = new[] { "SampleProject.Advanced" })]

namespace SampleProject.Advanced;

public class Account
{
    public Guid AggregateId { get; init; }
}

public class Transfer
{
    public Account? Customer { get; init; }
}

public class LegacyTransfer
{
    public Account? Legacy { get; init; }
}

public static class StructuralExamples
{
    public static void Demo()
    {
        var transfer = new Transfer { Customer = new Account { AggregateId = Guid.NewGuid() } };
        if (Generated.TransferAccountResolver.TryGet(transfer, out Guid? aggregateId))
        {
            Console.WriteLine(aggregateId);
        }

        // Stateless logging-oriented dispatch, independent of the shared registry.
        if (Generated.PropertyResolverDispatch.TryResolve("Customer.AggregateId", transfer, out var value))
        {
            Console.WriteLine(value);
        }
    }
}
