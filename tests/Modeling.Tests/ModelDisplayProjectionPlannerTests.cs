using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Modeling.Metadata;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public sealed class ModelDisplayProjectionPlannerTests
{
    private readonly IModelDisplayProjectionPlanner _planner = new ModelDisplayProjectionPlanner();

    public static TheoryData<ModelDefinition, IReadOnlyCollection<ModelDefinition>, Type> SupportedPlans => new()
    {
        { Source(Field("CompanyId", FieldKind.Reference, FieldValueType.ObjectId, Display("reference", "Company", "EID", "ENAME"))), [Target("Company")], typeof(LookupFieldDisplayProjectionPlan) },
        { Source(Field("ReviewerUserEID", FieldKind.Scalar, FieldValueType.User, Display("user", "IdentityAccount", "EID", "ENAME"))), [], typeof(LookupFieldDisplayProjectionPlan) },
        { Source(Field("OccurredAt", FieldKind.Scalar, FieldValueType.Instant, Display("instant", format: "date"))), [], typeof(TemporalFieldDisplayProjectionPlan) },
        { Source(Field("OccurredAt", FieldKind.Scalar, FieldValueType.Instant, Display("instant", format: "dateTime"))), [], typeof(TemporalFieldDisplayProjectionPlan) }
    };

    public static TheoryData<ModelDefinition, IReadOnlyCollection<ModelDefinition>, string> InvalidRules => new()
    {
        { Source(Field("CompanyId", FieldKind.Reference, FieldValueType.ObjectId, Display("computed"))), [], ModelDisplayProjectionDiagnosticCodes.TypeUnsupported },
        { Source(Field("CompanyId", FieldKind.Scalar, FieldValueType.ObjectId, Display("reference", "Company", "EID", "ENAME"))), [Target("Company")], ModelDisplayProjectionDiagnosticCodes.SourceSemanticsMismatch },
        { Source(Field("CompanyId", FieldKind.Reference, FieldValueType.ObjectId, Display("reference", targetModel: ""))), [], ModelDisplayProjectionDiagnosticCodes.LookupParametersRequired },
        { Source(Field("ReviewerUserEID", FieldKind.Scalar, FieldValueType.User, Display("user", "IdentityAccount", "EID", ""))), [], ModelDisplayProjectionDiagnosticCodes.UserTargetInvalid },
        { Source(Field("OccurredAt", FieldKind.Scalar, FieldValueType.Instant, Display("instant"))), [], ModelDisplayProjectionDiagnosticCodes.TemporalFormatRequired },
        { Source(Field("OccurredAt", FieldKind.Scalar, FieldValueType.Instant, Display("instant", targetModel: "Company", format: "dateTime"))), [], ModelDisplayProjectionDiagnosticCodes.ParametersMutuallyExclusive },
        { Source(Field("CompanyId", FieldKind.Reference, FieldValueType.ObjectId, Display("reference", "Company", "EID", "ENAME")), Field("CompanyId_DisplayValue", FieldKind.Scalar, FieldValueType.Text)), [Target("Company")], ModelDisplayProjectionDiagnosticCodes.CompanionNameConflict }
    };

    [Theory]
    [MemberData(nameof(SupportedPlans))]
    public void Compile_SupportedRule_ReturnsTypedCanonicalPlan(
        ModelDefinition source,
        IReadOnlyCollection<ModelDefinition> catalog,
        Type expectedPlanType)
    {
        var result = _planner.Compile(source, catalog);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
        var fieldPlan = Assert.Single(result.Plan!.Fields);
        Assert.IsType(expectedPlanType, fieldPlan);
        Assert.Equal(source.Fields[0].Name, fieldPlan.SourceField);
        Assert.Equal($"{source.Fields[0].Name}_DisplayValue", fieldPlan.CompanionField);
    }

    [Fact]
    public void Compile_AuthoringDocument_ReturnsSameTypedPlan()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Identity",
            Model = new ModelMetadataModel
            {
                Name = "IdentitySecurityEvent",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "OccurredAt",
                        Storage = new ModelMetadataStorageSection { Type = "datetime" },
                        Contract = new ModelMetadataContractSection { ValueType = "instant" },
                        Display = new ModelMetadataDisplayRule { Type = "instant", Format = "dateTime" }
                    }
                ]
            }
        };

        var result = _planner.Compile(document, []);

        Assert.True(result.IsSuccess);
        var plan = Assert.IsType<TemporalFieldDisplayProjectionPlan>(Assert.Single(result.Plan!.Fields));
        Assert.Equal("OccurredAt_DisplayValue", plan.CompanionField);
        Assert.Equal(TemporalDisplayFormat.DateTime, plan.Format);
    }

    [Fact]
    public void Compile_InvalidAuthoringDisplay_ReturnsDiagnosticInsteadOfPartialPlan()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Organization",
            Model = new ModelMetadataModel
            {
                Name = "Person",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "CompanyId",
                        Storage = new ModelMetadataStorageSection { Type = "guid" },
                        Display = new ModelMetadataDisplayRule
                        {
                            Type = "reference",
                            TargetModel = "Company",
                            TargetIdField = "EID",
                            TargetDisplayField = "ENAME"
                        }
                    }
                ]
            }
        };

        var result = _planner.Compile(document, [Target("Company")]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ModelDisplayProjectionDiagnosticCodes.SourceSemanticsMismatch, diagnostic.Code);
        Assert.Equal("CompanyId", diagnostic.FieldName);
    }

    [Fact]
    public void Compile_LookupRules_CarryControlledTargetsAndKinds()
    {
        var source = Source(
            Field("CompanyId", FieldKind.Reference, FieldValueType.ObjectId, Display("reference", "Company", "EID", "ENAME")),
            Field("ReviewerUserEID", FieldKind.Scalar, FieldValueType.User, Display("user", "IdentityAccount", "EID", "ENAME")));

        var result = _planner.Compile(source, [Target("Company")]);

        Assert.True(result.IsSuccess);
        var reference = Assert.IsType<LookupFieldDisplayProjectionPlan>(result.Plan!.Fields[0]);
        Assert.Equal(LookupDisplayProjectionKind.Reference, reference.Kind);
        Assert.Equal("Company", reference.TargetModel);
        Assert.Equal("EID", reference.TargetIdField);
        Assert.Equal("ENAME", reference.TargetDisplayField);
        var user = Assert.IsType<LookupFieldDisplayProjectionPlan>(result.Plan.Fields[1]);
        Assert.Equal(LookupDisplayProjectionKind.User, user.Kind);
        Assert.Equal("IdentityAccount", user.TargetModel);
    }

    [Theory]
    [InlineData("date", TemporalDisplayFormat.Date)]
    [InlineData("dateTime", TemporalDisplayFormat.DateTime)]
    public void Compile_InstantRule_CarriesControlledTemporalFormat(
        string authoredFormat,
        TemporalDisplayFormat expectedFormat)
    {
        var source = Source(
            Field("OccurredAt", FieldKind.Scalar, FieldValueType.Instant, Display("instant", format: authoredFormat)));

        var result = _planner.Compile(source, []);

        Assert.True(result.IsSuccess);
        var plan = Assert.IsType<TemporalFieldDisplayProjectionPlan>(Assert.Single(result.Plan!.Fields));
        Assert.Equal(expectedFormat, plan.Format);
    }

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public void Compile_InvalidRule_ReturnsStableLocatableDiagnostic(
        ModelDefinition source,
        IReadOnlyCollection<ModelDefinition> catalog,
        string expectedCode)
    {
        var result = _planner.Compile(source, catalog);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Equal("Person", diagnostic.ModelName);
        Assert.Equal(source.Fields[0].Name, diagnostic.FieldName);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
    }

    [Fact]
    public void Compile_ReferenceTargetIsMissing_ReturnsCatalogDiagnostic()
    {
        var source = Source(
            Field("CompanyId", FieldKind.Reference, FieldValueType.ObjectId, Display("reference", "Company", "EID", "ENAME")));

        var result = _planner.Compile(source, []);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ModelDisplayProjectionDiagnosticCodes.TargetModelMissing, diagnostic.Code);
        Assert.Equal("CompanyId", diagnostic.FieldName);
    }

    [Theory]
    [InlineData(false, true, "EID")]
    [InlineData(true, false, "ENAME")]
    public void Compile_ReferenceTargetFieldIsMissing_ReturnsCatalogDiagnostic(
        bool includeId,
        bool includeDisplay,
        string expectedField)
    {
        var source = Source(
            Field("CompanyId", FieldKind.Reference, FieldValueType.ObjectId, Display("reference", "Company", "EID", "ENAME")));

        var result = _planner.Compile(source, [Target("Company", includeId, includeDisplay)]);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ModelDisplayProjectionDiagnosticCodes.TargetFieldMissing, diagnostic.Code);
        Assert.Contains(expectedField, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_UndeclaredReferenceAndEnum_DoNotReceiveCompanions()
    {
        var source = Source(
            Field("CompanyId", FieldKind.Reference, FieldValueType.ObjectId),
            Field("Status", FieldKind.Scalar, FieldValueType.Enum));

        var result = _planner.Compile(source, []);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Plan!.Fields);
    }

    private static FieldDisplayRuleDefinition Display(
        string type,
        string targetModel = "",
        string targetIdField = "",
        string targetDisplayField = "",
        string format = "") => new(type, targetModel, targetIdField, targetDisplayField, format);

    private static FieldDefinition Field(
        string name,
        FieldKind kind,
        FieldValueType valueType,
        FieldDisplayRuleDefinition? display = null) =>
        new(name, name, kind, valueType, "string", false, false, false, display);

    private static ModelDefinition Source(params FieldDefinition[] fields) =>
        new("Person", "Person", ModelKind.Entity, fields, []);

    private static ModelDefinition Target(
        string name,
        bool includeId = true,
        bool includeDisplay = true)
    {
        var fields = new List<FieldDefinition>();
        if (includeId)
        {
            fields.Add(Field("EID", FieldKind.Scalar, FieldValueType.ObjectId));
        }

        if (includeDisplay)
        {
            fields.Add(Field("ENAME", FieldKind.Scalar, FieldValueType.Text));
        }

        return new ModelDefinition(name, name, ModelKind.Entity, fields, []);
    }
}
