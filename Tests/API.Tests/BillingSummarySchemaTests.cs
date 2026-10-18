namespace Tests.API.Tests;

[TestFixture]
public sealed class BillingSummarySchemaTests
{
    [Test]
    public void OpenApiDeclaresTheBillingFlagsAlwaysReturnedByTheSummaryContract()
    {
        var schema = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "openapi.yml"));
        var billingSummary = schema
            .Split("    BillingSummary:", StringSplitOptions.None)[1]
            .Split("    CheckoutRequest:", StringSplitOptions.None)[0]
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.That(billingSummary, Does.Contain("required: [plan, status, trialUsed, cancelAtPeriodEnd, isManualGrant, promptsUsed, promptLimit]"));
        Assert.That(billingSummary, Does.Contain("        cancelAtPeriodEnd:\n          type: boolean"));
        Assert.That(billingSummary, Does.Contain("        isManualGrant:\n          type: boolean"));
    }
}
