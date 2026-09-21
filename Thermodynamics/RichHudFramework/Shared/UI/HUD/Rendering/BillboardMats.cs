using System.Collections.Generic;
using VRage.Utils;
using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		namespace Rendering
		{
			public struct TriMaterial
			{
				public static readonly TriMaterial Default = new TriMaterial
				{
					textureID = Material.Default.TextureID,
					bbColor = Vector4.One,
/// <summary>Triangle operation.</summary>
					texCoords = new Triangle(
/// <summary>Vector2 operation.</summary>
						new Vector2(0f, 0f),
/// <summary>Vector2 operation.</summary>
						new Vector2(0f, 1f),
/// <summary>Vector2 operation.</summary>
						new Vector2(1f, 0f)
					)
				};

				public MyStringId textureID;

				public Vector4 bbColor;

				public Triangle texCoords;
			}

			public struct QuadMaterial
			{
/// <summary>QuadMaterial operation.</summary>
				public static readonly QuadMaterial Default = new QuadMaterial()
				{
					textureID = Material.Default.TextureID,
					bbColor = Vector4.One,
/// <summary>FlatQuad operation.</summary>
					texCoords = new FlatQuad(
/// <summary>Vector2 operation.</summary>
						new Vector2(0f, 0f),
/// <summary>Vector2 operation.</summary>
						new Vector2(0f, 1f),
/// <summary>Vector2 operation.</summary>
						new Vector2(1f, 0f),
/// <summary>Vector2 operation.</summary>
						new Vector2(1f, 1f)
					)
				};

				public MyStringId textureID;

				public Vector4 bbColor;

				public FlatQuad texCoords;
			}

			public struct BoundedQuadMaterial
			{
/// <summary>BoundedQuadMaterial operation.</summary>
				public static readonly BoundedQuadMaterial Default = new BoundedQuadMaterial()
				{
					textureID = Material.Default.TextureID,
					bbColor = Vector4.One,
/// <summary>BoundingBox2 operation.</summary>
					texBounds = new BoundingBox2(Vector2.Zero, Vector2.One)
				};

				public MyStringId textureID;

				public Vector4 bbColor;

				public BoundingBox2 texBounds;
			}

			public struct PolyMaterial
			{
/// <summary>PolyMaterial operation.</summary>
				public static readonly PolyMaterial Default = new PolyMaterial()
				{
					textureID = Material.Default.TextureID,
					bbColor = Vector4.One,
					texCoords = null
				};

				public MyStringId textureID;

				public Vector4 bbColor;

				public BoundingBox2 texBounds;

				public List<Vector2> texCoords;
			}

			public struct FlatQuad
			{
				public Vector2 Point0, Point1, Point2, Point3;

/// <summary>FlatQuad operation.</summary>
				public FlatQuad(Vector2 Point0, Vector2 Point1, Vector2 Point2, Vector2 Point3)
				{
					this.Point0 = Point0;
					this.Point1 = Point1;
					this.Point2 = Point2;
					this.Point3 = Point3;
				}
			}

			public struct Triangle
			{
				public Vector2 Point0, Point1, Point2;

/// <summary>Triangle operation.</summary>
				public Triangle(Vector2 Point0, Vector2 Point1, Vector2 Point2)
				{
					this.Point0 = Point0;
					this.Point1 = Point1;
					this.Point2 = Point2;
				}
			}

			public struct TriangleD
			{
				public Vector3D Point0, Point1, Point2;

/// <summary>TriangleD operation.</summary>
				public TriangleD(Vector3D Point0, Vector3D Point1, Vector3D Point2)
				{
					this.Point0 = Point0;
					this.Point1 = Point1;
					this.Point2 = Point2;
				}
			}
		}
	}
}