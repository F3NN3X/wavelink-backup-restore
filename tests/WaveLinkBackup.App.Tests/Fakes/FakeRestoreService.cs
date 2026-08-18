using WaveLinkBackup.App.Services;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Tests.Fakes;

/// <summary>
/// A no-op restore service for window-construction tests that hand a MainWindow to the harness but
/// never drive a restore. Task 6 made IRestoreService a required constructor parameter, so every
/// test that builds a window now needs SOME instance here - one that throws if actually called,
/// which is exactly what "a restore was unexpectedly driven" should look like in a test that does
/// not exercise the flow.
/// </summary>
public sealed class FakeRestoreService : IRestoreService
{
    public Task<Result<RestorePlan>> PlanAsync(string snapshotId, SettingsInspection live, CancellationToken ct) =>
        throw new NotSupportedException("FakeRestoreService does not plan.");

    public Task<RestoreResultView> RestoreAsync(
        string snapshotId, SettingsInspection live, IProgress<RestoreStage>? progress, CancellationToken ct) =>
        throw new NotSupportedException("FakeRestoreService does not restore.");
}
