using ICP.Platform.Crud.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class EfPersistenceConstraintClassifierTests
{
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", 547)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", 2601)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", 2627)]
    [InlineData("MySql.EntityFrameworkCore", 1062)]
    [InlineData("MySql.EntityFrameworkCore", 1451)]
    [InlineData("MySql.EntityFrameworkCore", 1452)]
    [InlineData("Oracle.EntityFrameworkCore", 1)]
    [InlineData("Oracle.EntityFrameworkCore", 2291)]
    [InlineData("Oracle.EntityFrameworkCore", 2292)]
    public void IsKnownConstraintConflict_RecognizesOnlyDocumentedProviderConstraintNumbers(
        string providerName,
        int number)
    {
        var exception = new DbUpdateException("write failed", new NumberedProviderException(number));

        Assert.True(EfPersistenceConstraintClassifier.IsKnownConstraintConflict(providerName, exception));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", 515)]
    [InlineData("Unknown.Provider", 547)]
    public void IsKnownConstraintConflict_RejectsUnknownPersistenceFailures(string providerName, int number)
    {
        var exception = new DbUpdateException("write failed", new NumberedProviderException(number));

        Assert.False(EfPersistenceConstraintClassifier.IsKnownConstraintConflict(providerName, exception));
    }

    private sealed class NumberedProviderException(int number) : Exception
    {
        public int Number { get; } = number;
    }
}
