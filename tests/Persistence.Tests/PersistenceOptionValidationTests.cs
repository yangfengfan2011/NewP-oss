using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Extensions;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class PersistenceOptionValidationTests
{
    [Fact]
    public void Validate_ThrowsWhenAdapterMissing()
    {
        var options = new PersistenceOptions
        {
            Provider = PersistenceProvider.SqlServer,
            Adapter = null
        };

        var ex = Assert.Throws<InvalidOperationException>(() => PersistenceOptionValidation.Validate(options));
        Assert.Contains("Adapter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
