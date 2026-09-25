using VRage.Utils;
using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		namespace Rendering
		{
			public struct CroppedBox
			{
				public static readonly BoundingBox2 defaultMask =

					new BoundingBox2(-Vector2.PositiveInfinity, Vector2.PositiveInfinity);

				public BoundingBox2 bounds;

				public BoundingBox2? mask;
			}

			public struct QuadBoardData
			{
				public BoundedQuadMaterial material;
				public MyQuadD positions;
			}

			public struct BoundedQuadBoard
			{
				public BoundingBox2 bounds;
				public QuadBoard quadBoard;
			}

			public struct QuadBoard
			{
				public static readonly QuadBoard Default;

				public float skewRatio;

				public BoundedQuadMaterial materialData;


				static QuadBoard()
				{

					var matFit = new BoundingBox2(new Vector2(0f, 0f), new Vector2(1f, 1f));

					Default = new QuadBoard(Material.Default.TextureID, matFit, Color.White);
				}


				public QuadBoard(MyStringId textureID, BoundingBox2 matFit, Vector4 bbColor, float skewRatio = 0f)
				{
					materialData.textureID = textureID;
					materialData.texBounds = matFit;
					materialData.bbColor = bbColor;
					this.skewRatio = skewRatio;
				}


				public QuadBoard(MyStringId textureID, BoundingBox2 matFit, Color color, float skewRatio = 0f)
				{
					materialData.textureID = textureID;
					materialData.texBounds = matFit;
					materialData.bbColor = color.GetBbColor();
					this.skewRatio = skewRatio;
				}
			}
		}
	}
}