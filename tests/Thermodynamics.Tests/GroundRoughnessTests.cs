using Thermodynamics.Core;
using Xunit;

namespace Thermodynamics.Tests
{
    public class GroundRoughnessTests
    {
        private const float WorldSetting = 0.03f;

        [Fact]
/// <summary>TheOrderingMatchesTheWindEngineeringTable operation.</summary>
        public void TheOrderingMatchesTheWindEngineeringTable()
        {
            float ice = GroundTemperature.RoughnessFor("Ice_01", WorldSetting);
            float snow = GroundTemperature.RoughnessFor("Snow", WorldSetting);
            float sand = GroundTemperature.RoughnessFor("Sand_01", WorldSetting);
            float grass = GroundTemperature.RoughnessFor("Grass", WorldSetting);
            float rock = GroundTemperature.RoughnessFor("Stone_02", WorldSetting);
            float forest = GroundTemperature.RoughnessFor("Woods", WorldSetting);

            Assert.True(ice < snow, "open ice is smoother than snow");
            Assert.True(snow < sand, "snow is smoother than sand");
            Assert.True(sand < grass, "sand is smoother than grassland");
            Assert.True(grass < rock, "grassland is smoother than broken rock");
            Assert.True(rock < forest, "rock is smoother than forest");

            Assert.True(forest > ice * 1000f,
                "forest should be orders of magnitude rougher than ice: " + forest + " against " + ice);
        }

        [Fact]
/// <summary>UnknownGroundKeepsTheWorldSetting operation.</summary>
        public void UnknownGroundKeepsTheWorldSetting()
        {
            Assert.Equal(WorldSetting, GroundTemperature.RoughnessFor("Unobtainium", WorldSetting), 6);
            Assert.Equal(WorldSetting, GroundTemperature.RoughnessFor("", WorldSetting), 6);
            Assert.Equal(WorldSetting, GroundTemperature.RoughnessFor(null, WorldSetting), 6);
        }

        [Fact]
/// <summary>RougherGroundSlowsTheWindNearIt operation.</summary>
        public void RougherGroundSlowsTheWindNearIt()
        {
            float snow = GroundTemperature.RoughnessFor("Snow", WorldSetting);
            float forest = GroundTemperature.RoughnessFor("Woods", WorldSetting);

            Assert.True(WindProfile.Multiplier(2f, forest, 600f) < WindProfile.Multiplier(2f, snow, 600f),
                "two metres over forest should be slower than two metres over snow");

            Assert.True(WindProfile.Multiplier(600f, forest, 600f) > WindProfile.Multiplier(600f, snow, 600f),
                "at the gradient height the rough profile should have climbed further");
        }

        [Fact]
/// <summary>EveryClassifiedMaterialCarriesOne operation.</summary>
        public void EveryClassifiedMaterialCarriesOne()
        {
            string[] materials =
            {
                "snow", "ice", "frozen", "tundra", "sand", "desert", "dune",
                "lava", "magma", "grass", "soil", "dirt", "rock", "stone", "woods", "forest",
            };

            foreach (string material in materials)
            {
                float roughness = GroundTemperature.RoughnessFor(material, WorldSetting);
                Assert.True(roughness > 0f, material + " has no roughness");
                Assert.True(roughness < 2f, material + " is rougher than any real ground: " + roughness);
            }
        }
    }
}
