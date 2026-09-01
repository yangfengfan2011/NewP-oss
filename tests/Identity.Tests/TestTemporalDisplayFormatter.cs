using System.Globalization;
using ICP.Platform.Presentation;

namespace ICP.Platform.Identity.Tests;

internal sealed class TestTemporalDisplayFormatter : ITemporalDisplayFormatter
{
    public string FormatInstant(DateTime instantUtc) =>
        instantUtc.ToString("O", CultureInfo.InvariantCulture);
}
