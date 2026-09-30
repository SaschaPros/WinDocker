namespace WinDocker.Core.Models;

/// <summary>What a prune removed.</summary>
/// <param name="DeletedCount">Number of containers, images or volumes that were removed.</param>
/// <param name="SpaceReclaimed">Disk space that was freed, in bytes.</param>
public sealed record PruneResult(int DeletedCount, long SpaceReclaimed);
