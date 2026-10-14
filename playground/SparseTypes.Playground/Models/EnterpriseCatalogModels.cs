using SparseTypes;

namespace SparseTypes.Playground.Models;

[SparseModel]
public partial class EnterpriseCatalogItem
{
    public CatalogOverview Overview { get; set; } = new();
    public CatalogCommercialDetails Commercial { get; set; } = new();
    public CatalogOperationsDetails Operations { get; set; } = new();
    public CatalogWorkflow Workflow { get; set; } = new();
    public List<CatalogWorkMaster> WorkMasters { get; set; } = [];
}

[SparseModel]
public partial class CatalogOverview
{
    public string ItemCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public List<string> AlternateCodes { get; set; } = [];
}

[SparseModel]
public partial class CatalogCommercialDetails
{
    public string Customer { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string ContractCode { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal UnitPrice { get; set; }
    public int AnnualVolume { get; set; }
    public List<CatalogPriceTier> PriceTiers { get; set; } = [];
}

[SparseModel]
public partial class CatalogOperationsDetails
{
    public string Facility { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    public int LeadTimeDays { get; set; }
    public string QualityStandard { get; set; } = string.Empty;
    public bool InspectionRequired { get; set; }
    public string PackagingInstruction { get; set; } = string.Empty;
    public List<CatalogOperationDocument> Documents { get; set; } = [];
}

[SparseModel]
public partial class CatalogPriceTier
{
    [SparseKey]
    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public int MinimumQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public List<CatalogDiscountBand> Discounts { get; set; } = [];
}

[SparseModel]
public partial class CatalogDiscountBand
{
    [SparseKey]
    public string Code { get; set; } = string.Empty;

    public int MinimumQuantity { get; set; }
    public decimal DiscountPercent { get; set; }
    public string ApprovalRole { get; set; } = string.Empty;
}

[SparseModel]
public partial class CatalogOperationDocument
{
    [SparseKey]
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public bool Required { get; set; }
}

[SparseModel]
public partial class CatalogWorkflow
{
    public List<CatalogWorkStep> Steps { get; set; } = [];
}

[SparseModel]
public partial class CatalogWorkStep
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string ResponsibleTeam { get; set; } = string.Empty;
    public decimal StandardHours { get; set; }
}

[SparseModel]
public partial class CatalogWorkMaster
{
    [SparseKey]
    public string StepId { get; set; } = string.Empty;

    public string WorkCenter { get; set; } = string.Empty;
    public string Equipment { get; set; } = string.Empty;
    public decimal SetupMinutes { get; set; }
    public string Instruction { get; set; } = string.Empty;
    public bool SupervisorApproval { get; set; }
}
