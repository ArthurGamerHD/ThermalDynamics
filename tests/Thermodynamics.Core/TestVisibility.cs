using System.Runtime.CompilerServices;

// The only source file of this project that does not live under Data/Scripts, and it is deliberate:
// the game must never compile it. `InternalsVisibleTo` is not on the script whitelist, and the mod
// has no use for it — this exists so the test assembly can drive the internal seams a pass uses,
// such as filling a RoomMap room by room, without those seams having to become public API that the
// mod itself never calls. See rules.md, D2: an API with no caller but a test is still an API.
[assembly: InternalsVisibleTo("Thermodynamics.Tests")]
[assembly: InternalsVisibleTo("Thermodynamics.Harness")]
