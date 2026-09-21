using RichHudFramework.UI.Rendering;
using VRageMath;

namespace RichHudFramework.UI
{
	public class TexturedBox : HudElementBase
	{
		public Material Material { get { return hudBoard.Material; } set { hudBoard.Material = value; } }

		public MaterialAlignment MatAlignment { get { return hudBoard.MatAlignment; } set { hudBoard.MatAlignment = value; } }

		public Color Color { get { return hudBoard.Color; } set { hudBoard.Color = value; } }

		protected readonly MatBoard hudBoard;

/// <summary>TexturedBox operation.</summary>
		public TexturedBox(HudParentBase parent) : base(parent)
		{
/// <summary>MatBoard operation.</summary>
			hudBoard = new MatBoard();
/// <summary>Vector2 operation.</summary>
			Size = new Vector2(50f);
		}

/// <summary>TexturedBox operation.</summary>
		public TexturedBox() : this(null)
		{ }

/// <summary>Draw operation.</summary>
		protected override void Draw()
		{
			if (hudBoard.Color.A > 0)
			{
/// <summary>default operation.</summary>
				CroppedBox box = default(CroppedBox);
				Vector2 halfSize = (UnpaddedSize) * .5f;

/// <summary>BoundingBox2 operation.</summary>
				box.bounds = new BoundingBox2(Position - halfSize, Position + halfSize);
				box.mask = MaskingBox;
				hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);
			}
		}
	}
}