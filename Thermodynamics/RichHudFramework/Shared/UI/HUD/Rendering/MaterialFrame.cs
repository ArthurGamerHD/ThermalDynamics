using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		namespace Rendering
		{
			public interface IReadOnlyMaterialFrame
			{
				Material Material { get; }

				MaterialAlignment Alignment { get; }

/// <summary>Returns the materialalignment.</summary>
				BoundingBox2 GetMaterialAlignment(float bbAspectRatio);

/// <summary>Returns the alignmentscale.</summary>
				Vector2 GetAlignmentScale(float bbAspectRatio);
			}

			public class MaterialFrame : IReadOnlyMaterialFrame
			{
				public Material Material { get; set; }

				public MaterialAlignment Alignment { get; set; }

/// <summary>MaterialFrame operation.</summary>
				public MaterialFrame()
				{
					Material = Material.Default;
					Alignment = MaterialAlignment.StretchToFit;
				}

/// <summary>Returns the materialalignment.</summary>
				public BoundingBox2 GetMaterialAlignment(float bbAspectRatio)
				{
					BoundingBox2 bounds = Material.UVBounds;

					if (Alignment != MaterialAlignment.StretchToFit)
					{
/// <summary>Vector2 operation.</summary>
						Vector2 uvScale = new Vector2(1f);
						float matAspectRatio = Material.Size.X / Material.Size.Y;

						if (Alignment == MaterialAlignment.FitAuto)
						{
							if (matAspectRatio > bbAspectRatio) // Material is wider than target; crop width (U)
/// <summary>Vector2 operation.</summary>
								uvScale = new Vector2(1f, matAspectRatio / bbAspectRatio);
							else // Material is taller than target; crop height (V)
/// <summary>Vector2 operation.</summary>
								uvScale = new Vector2(bbAspectRatio / matAspectRatio, 1f);
						}
/// <summary>if operation.</summary>
						else if (Alignment == MaterialAlignment.FitVertical)
						{
/// <summary>Vector2 operation.</summary>
							uvScale = new Vector2(bbAspectRatio / matAspectRatio, 1f);
						}
/// <summary>if operation.</summary>
						else if (Alignment == MaterialAlignment.FitHorizontal)
						{
/// <summary>Vector2 operation.</summary>
							uvScale = new Vector2(1f, matAspectRatio / bbAspectRatio);
						}

						bounds.Scale(uvScale);
					}

					return bounds;
				}

/// <summary>Returns the alignmentscale.</summary>
				public Vector2 GetAlignmentScale(float bbAspectRatio)
				{
					if (Alignment != MaterialAlignment.StretchToFit)
					{
						float matAspectRatio = Material.Size.X / Material.Size.Y;
/// <summary>Vector2 operation.</summary>
						Vector2 bbScale = new Vector2(1f);

						if (Alignment == MaterialAlignment.FitAuto)
						{
							if (matAspectRatio < bbAspectRatio)
/// <summary>Vector2 operation.</summary>
								bbScale = new Vector2(matAspectRatio / bbAspectRatio, 1f);
							else
/// <summary>Vector2 operation.</summary>
								bbScale = new Vector2(1f, bbAspectRatio / matAspectRatio);
						}
/// <summary>if operation.</summary>
						else if (Alignment == MaterialAlignment.FitHorizontal)
						{
/// <summary>Vector2 operation.</summary>
							bbScale = new Vector2(1f, bbAspectRatio / matAspectRatio);
						}
/// <summary>if operation.</summary>
						else if (Alignment == MaterialAlignment.FitVertical)
						{
/// <summary>Vector2 operation.</summary>
							bbScale = new Vector2(matAspectRatio / bbAspectRatio, 1f);
						}

						return bbScale;
					}
					else
						return Vector2.One;
				}
			}
		}
	}
}