using Microsoft.EntityFrameworkCore.Design;

namespace RotmgTrackerV1.Core.Data;

public class TrackerDbContextFactory : IDesignTimeDbContextFactory<TrackerDbContext>
{
    public TrackerDbContext CreateDbContext(string[] args)
        => new TrackerDbContext(Path.Combine(Path.GetTempPath(), "rotmg_design_time.db"));
}