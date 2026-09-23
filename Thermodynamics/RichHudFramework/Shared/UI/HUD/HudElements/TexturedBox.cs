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


		public TexturedBox(HudParentBase parent) : base(parent)
		{

			hudBoard = new MatBoard();

			Size = new Vector2(50f);
		}


		public TexturedBox() : this(null)
		{ }


		protected override void Draw()
		{
			if (hudBoard.Color.A > 0)
			{

				CroppedBox box = default(CroppedBox);
				Vector2 halfSize = (UnpaddedSize) * .5f;


				box.bounds = new BoundingBox2(Position - halfSize, Position + halfSize);
				box.mask = MaskingBox;
				hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);
			}
		}
	}
}