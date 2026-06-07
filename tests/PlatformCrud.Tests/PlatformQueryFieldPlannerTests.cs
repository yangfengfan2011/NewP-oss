using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class PlatformQueryFieldPlannerTests
{
    [Fact]
    public void Build_WhenFieldsContainDisplayValue_ComputesResponseAndDbFields()
    {
        var descriptor = Descriptor();

        var plan = PlatformQueryFieldPlanner.Build(
            descriptor,
            new PlatformQueryRequest(
                Fields: ["EID", "CompanyName", "IsEnabled"],
                Page: 1,
                PageSize: 20,
                SortBy: "PersonNo",
                Conditions:
                [
                    new PlatformQueryCondition("EmploymentStatus", "=", "FullTime")
                ]));

        Assert.Equal(["EID", "CompanyName", "IsEnabled"], plan.ResponseFields);
        Assert.Equal(["EID", "IsEnabled", "PersonNo", "EmploymentStatus", "CompanyId"], plan.DbSelectFields);
        Assert.Equal(["PersonNo", "EmploymentStatus", "CompanyId"], plan.InternalOnlyFields);
        var displayField = Assert.Single(plan.RequestedDisplayFields);
        Assert.Equal("CompanyName", displayField.Field);
        Assert.Equal("CompanyId", displayField.SourceField);
    }

    [Fact]
    public void Build_WhenFieldsAreEmpty_UsesDefaultFields()
    {
        var plan = PlatformQueryFieldPlanner.Build(Descriptor(), new PlatformQueryRequest(Fields: null));

        Assert.Equal(["EID", "PersonNo", "ENAME", "CompanyName"], plan.ResponseFields);
        Assert.Equal(["EID", "PersonNo", "ENAME", "CompanyId"], plan.DbSelectFields);
    }

    [Theory]
    [InlineData("SecretField", "PlatformQuery.UnknownField")]
    [InlineData("CompanyId", "PlatformQuery.DisplayValueSortOrFilterUnsupported")]
    public void Build_WhenInvalidFieldRequested_ThrowsStableCode(string field, string expectedCode)
    {
        var request = field == "SecretField"
            ? new PlatformQueryRequest(Fields: ["SecretField"])
            : new PlatformQueryRequest(Fields: ["EID"], SortBy: "CompanyName");

        var error = Assert.Throws<PlatformQueryValidationException>(() =>
            PlatformQueryFieldPlanner.Build(Descriptor(), request));

        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void Build_WhenConditionReferencesUnknownField_ThrowsStableCode()
    {
        var error = Assert.Throws<PlatformQueryValidationException>(() =>
            PlatformQueryFieldPlanner.Build(
                Descriptor(),
                new PlatformQueryRequest(
                    Fields: ["EID"],
                    Conditions: [new PlatformQueryCondition("MissingState", "=", true)])));

        Assert.Equal("PlatformQuery.UnknownConditionField", error.Code);
    }

    [Fact]
    public void Build_WhenIdFieldIsNotAllowedModelField_ThrowsStableCode()
    {
        var error = Assert.Throws<PlatformQueryValidationException>(() =>
            PlatformQueryFieldPlanner.Build(
                Descriptor(idField: "UnknownId"),
                new PlatformQueryRequest(Fields: ["EID"])));

        Assert.Equal("PlatformQuery.InvalidDescriptorIdField", error.Code);
    }

    [Fact]
    public void Build_WhenDisplayValueFieldNamesDuplicate_ThrowsStableCode()
    {
        var error = Assert.Throws<PlatformQueryValidationException>(() =>
            PlatformQueryFieldPlanner.Build(
                Descriptor(
                    displayValueFields:
                    [
                        new PlatformDisplayValueField("CompanyName", "CompanyId", "Company", "EID", "ENAME"),
                        new PlatformDisplayValueField("CompanyName", "DepartmentId", "Department", "EID", "ENAME")
                    ]),
                new PlatformQueryRequest(Fields: ["EID"])));

        Assert.Equal("PlatformQuery.DuplicateDisplayField", error.Code);
    }

    [Fact]
    public void Build_WhenDisplayValueFieldConflictsWithModelField_ThrowsStableCode()
    {
        var error = Assert.Throws<PlatformQueryValidationException>(() =>
            PlatformQueryFieldPlanner.Build(
                Descriptor(
                    displayValueFields:
                    [
                        new PlatformDisplayValueField("PersonNo", "CompanyId", "Company", "EID", "ENAME")
                    ]),
                new PlatformQueryRequest(Fields: ["EID"])));

        Assert.Equal("PlatformQuery.DisplayFieldConflictsWithModelField", error.Code);
    }

    [Fact]
    public void Build_WhenDisplayValueSourceFieldIsNotAllowedModelField_ThrowsStableCode()
    {
        var error = Assert.Throws<PlatformQueryValidationException>(() =>
            PlatformQueryFieldPlanner.Build(
                Descriptor(
                    displayValueFields:
                    [
                        new PlatformDisplayValueField("CompanyName", "MissingCompanyId", "Company", "EID", "ENAME")
                    ]),
                new PlatformQueryRequest(Fields: ["CompanyName"])));

        Assert.Equal("PlatformQuery.InvalidDisplaySourceField", error.Code);
    }

    private static PlatformQueryDescriptor Descriptor(
        string idField = "EID",
        IReadOnlyList<PlatformDisplayValueField>? displayValueFields = null) => new()
    {
        ResourceKey = "organization.person",
        ModelName = "Person",
        IdField = idField,
        AllowedModelFields = ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled"],
        DefaultFields = ["EID", "PersonNo", "ENAME", "CompanyName"],
        DefaultSortField = "ENAME",
        DisplayValueFields = displayValueFields ??
        [
            new PlatformDisplayValueField(
                Field: "CompanyName",
                SourceField: "CompanyId",
                TargetModelName: "Company",
                TargetIdField: "EID",
                TargetDisplayField: "ENAME")
        ]
    };
}
