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


				BoundingBox2 GetMaterialAlignment(float bbAspectRatio);


				Vector2 GetAlignmentScale(float bbAspectRatio);
			}

			public class MaterialFrame : IReadOnlyMaterialFrame
			{
				public Material Material { get; set; }

				public MaterialAlignment Alignment { get; set; }


				public MaterialFrame()
				{
					Material = Material.Default;
					Alignment = MaterialAlignment.StretchToFit;
				}


				public BoundingBox2 GetMaterialAlignment(float bbAspectRatio)
				{
					BoundingBox2 bounds = Material.UVBounds;

					if (Alignment != MaterialAlignment.StretchToFit)
					{

						Vector2 uvScale = new Vector2(1f);
						float matAspectRatio = Material.Size.X / Material.Size.Y;

						if (Alignment == MaterialAlignment.FitAuto)
						{
							if (matAspectRatio > bbAspectRatio)

								uvScale = new Vector2(1f, matAspectRatio / bbAspectRatio);
							else

								uvScale = new Vector2(bbAspectRatio / matAspectRatio, 1f);
						}

						else if (Alignment == MaterialAlignment.FitVertical)
						{

							uvScale = new Vector2(bbAspectRatio / matAspectRatio, 1f);
						}

						else if (Alignment == MaterialAlignment.FitHorizontal)
						{

							uvScale = new Vector2(1f, matAspectRatio / bbAspectRatio);
						}

						bounds.Scale(uvScale);
					}

					return bounds;
				}


				public Vector2 GetAlignmentScale(float bbAspectRatio)
				{
					if (Alignment != MaterialAlignment.StretchToFit)
					{
						float matAspectRatio = Material.Size.X / Material.Size.Y;

						Vector2 bbScale = new Vector2(1f);

						if (Alignment == MaterialAlignment.FitAuto)
						{
							if (matAspectRatio < bbAspectRatio)

								bbScale = new Vector2(matAspectRatio / bbAspectRatio, 1f);
							else

								bbScale = new Vector2(1f, bbAspectRatio / matAspectRatio);
						}

						else if (Alignment == MaterialAlignment.FitHorizontal)
						{

							bbScale = new Vector2(1f, bbAspectRatio / matAspectRatio);
						}

						else if (Alignment == MaterialAlignment.FitVertical)
						{

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