using Thermodynamics.Core;
using Thermodynamics.Harness;
using VRageMath;
using Xunit;

namespace Thermodynamics.Tests
{
    /// <summary>
    /// The audit is the diagnostic that answers "the room mapper says zero rooms, why". It has to
    /// be right about a healthy grid before it can be trusted about a broken one.
    /// </summary>
    public class RoomAuditTests
    {
        private static ThermalSimulation SealedShell()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));
            return builder.BuildSimulation(new ThermalSettings());
        }

        [Fact]
        public void AHealthyShellAuditsClean()
        {
            ThermalSimulation simulation = SealedShell();
            RoomAudit audit = simulation.AuditRooms();

            Assert.False(audit.HasLeak);
            Assert.Equal(0, audit.LeakedCells);
            Assert.Equal(0, audit.OpenBlockCells);
            Assert.Equal(26, audit.BlockCells);
            Assert.Equal(26, audit.SolidCells);
            Assert.Equal(1, audit.RoomCells);
            Assert.Equal(1, audit.RoomCount);
            Assert.Equal(0, audit.UnsealedBlockFaces);
            Assert.Equal(0, audit.BlocksSealingNothing);

            // padded search box is (-2..2)^3
            Assert.Equal(125, audit.SearchVolume);
            Assert.Equal(audit.SearchVolume, audit.ExternalCells + audit.SolidCells + audit.RoomCells);
        }

        [Fact]
        public void ABlockThatSealsNothingIsCountedAndNamed()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));

            // replace one wall block with a lattice: mounts everywhere, seals nothing
            BlockInstance solid = builder.Grid.GetAtCell(new Vector3I(0, 0, -1));
            builder.Grid.Remove(solid);
            builder.Place(Catalog.Grating(), new Vector3I(0, 0, -1));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());
            RoomAudit audit = simulation.AuditRooms();

            Assert.Equal(0, audit.RoomCount);

            // the lattice seals nothing, so the fill walks straight through it: legitimately
            // open space, and the reason the room is gone
            Assert.False(audit.HasLeak);
            Assert.Equal(1, audit.OpenBlockCells);
            Assert.Equal(1, audit.BlocksSealingNothing);
            Assert.Equal(Face.Count, audit.UnsealedBlockFaces);
            Assert.Single(audit.Examples);
            Assert.Contains("Grating", audit.Examples[0]);
        }

        [Fact]
        public void AnOpenDoorLeaksAndTheExampleSaysSo()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-1, -1, -1), new Vector3I(2, 2, 2));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());
            BlockInstance door = builder.Grid.GetAtCell(new Vector3I(0, 0, -1));

            door.IsSealedByDoorState = false;
            simulation.RefreshBlockSealing(door);
            simulation.Rooms.RunToCompletion();

            RoomAudit audit = simulation.AuditRooms();

            Assert.Equal(0, audit.RoomCount);
            Assert.Equal(1, audit.OpenBlockCells);
            Assert.Contains("no (door)", audit.Examples[0]);
        }

        [Fact]
        public void TheExampleListIsBounded()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.Grating(), new Vector3I(-3, -3, -3), new Vector3I(4, 4, 4));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());
            RoomAudit audit = simulation.AuditRooms(4);

            Assert.True(audit.OpenBlockCells > 4);
            Assert.Equal(4, audit.Examples.Count);
        }

        [Fact]
        public void EveryCellOfTheSearchBoxIsClassifiedExactlyOnce()
        {
            GridBuilder builder = GridBuilder.Large();
            builder.Shell(Catalog.LightArmor(), new Vector3I(-2, -2, -2), new Vector3I(3, 3, 3));
            builder.Place(Catalog.Reactor(), new Vector3I(0, 0, 3));

            ThermalSimulation simulation = builder.BuildSimulation(new ThermalSettings());
            RoomAudit audit = simulation.AuditRooms();

            Assert.Equal(audit.SearchVolume, audit.ExternalCells + audit.SolidCells + audit.RoomCells);
            Assert.False(audit.HasLeak);
        }
    }
}
