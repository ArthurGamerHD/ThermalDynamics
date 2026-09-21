using VRage.Utils;
using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		namespace Rendering
		{
			public enum MaterialAlignment : int
			{
				StretchToFit = 0,

				FitVertical = 1,

				FitHorizontal = 2,

				FitAuto = 3,
			}

			public class Material
			{
/// <summary>Material operation.</summary>
				public static readonly Material Default = new Material("RichHudDefault", new Vector2(4f, 4f));

/// <summary>Material operation.</summary>
				public static readonly Material CircleMat = new Material("RhfCircle", new Vector2(1024f));

/// <summary>Material operation.</summary>
				public static readonly Material AnnulusMat = new Material("RhfAnnulus", new Vector2(1024f));

				public readonly MyStringId TextureID;

				public readonly Vector2 Size;

				public readonly BoundingBox2 UVBounds;

/// <summary>Material operation.</summary>
				public Material(string SubtypeId, Vector2 size) : this(MyStringId.GetOrCompute(SubtypeId), size)
				{ }

/// <summary>Material operation.</summary>
				public Material(string SubtypeId, Vector2 texSize, Vector2 texCoords, Vector2 size)
					: this(MyStringId.GetOrCompute(SubtypeId), texSize, texCoords, size)
				{ }

/// <summary>Material operation.</summary>
				public Material(MyStringId TextureID, Vector2 size)
				{
					this.TextureID = TextureID;
					this.Size = size;
/// <summary>BoundingBox2 operation.</summary>
					UVBounds = new BoundingBox2(Vector2.Zero, Vector2.One);
				}

/// <summary>Material operation.</summary>
				public Material(MyStringId SubtypeId, Vector2 texSize, Vector2 offset, Vector2 size)
				{
					this.TextureID = SubtypeId;
					this.Size = size;

					Vector2 rcpTexSize = 1f / texSize,
						halfUVSize = .5f * size * rcpTexSize,
						uvOffset = (offset * rcpTexSize) + halfUVSize;

/// <summary>BoundingBox2 operation.</summary>
					UVBounds = new BoundingBox2(uvOffset - halfUVSize, uvOffset + halfUVSize);
				}
			}
		}
	}
}