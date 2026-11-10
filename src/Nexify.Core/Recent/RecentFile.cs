using Nexify.Core.Media;

namespace Nexify.Core.Recent;

public sealed record RecentFile(string Path, MediaKind Kind, DateTimeOffset OpenedAtUtc);
