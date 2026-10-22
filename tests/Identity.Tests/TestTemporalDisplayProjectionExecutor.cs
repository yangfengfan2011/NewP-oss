using System.Globalization;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Presentation;

namespace ICP.Platform.Identity.Tests;

internal sealed class TestTemporalDisplayProjectionExecutor : ITemporalDisplayProjectionExecutor
{
    public TemporalDisplayProjectionResult Project(
        TemporalFieldDisplayProjectionPlan plan,
        DateTime rawValue) =>
        new(rawValue, rawValue.ToString("O", CultureInfo.InvariantCulture));
}
