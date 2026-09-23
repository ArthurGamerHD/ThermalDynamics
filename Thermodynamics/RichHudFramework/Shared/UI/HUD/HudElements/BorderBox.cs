using RichHudFramework.UI.Rendering;
using VRageMath;

namespace RichHudFramework.UI
{
	public class BorderBox : HudElementBase
    {
		public Material Material { get { return hudBoard.Material; } set { hudBoard.Material = value; } }

		public MaterialAlignment MatAlignment { get { return hudBoard.MatAlignment; } set { hudBoard.MatAlignment = value; } }

		public Color Color { get { return hudBoard.Color; } set { hudBoard.Color = value; } }

        public float Thickness { get; set; }

		protected readonly MatBoard hudBoard;


		public BorderBox(HudParentBase parent) : base(parent)
        {

            hudBoard = new MatBoard();
            Thickness = 1f;
        }


        public BorderBox() : this(null)
        { }


        protected override void Draw()
        {
            if (Color.A > 0)
            {

                CroppedBox box = default(CroppedBox);
                box.mask = MaskingBox;

                float height = UnpaddedSize.Y, 
                    width = UnpaddedSize.X;
                Vector2 halfSize, pos;


                halfSize = new Vector2(Thickness, height) * .5f;

                pos = Position + new Vector2((-width + Thickness) * .5f, 0f);

                box.bounds = new BoundingBox2(pos - halfSize, pos + halfSize);
                hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);


                halfSize = new Vector2(width, Thickness) * .5f;

                pos = Position + new Vector2(0f, (height - Thickness) * .5f);

                box.bounds = new BoundingBox2(pos - halfSize, pos + halfSize);
                hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);


                halfSize = new Vector2(Thickness, height) * .5f;

                pos = Position + new Vector2((width - Thickness) * .5f, 0f);

                box.bounds = new BoundingBox2(pos - halfSize, pos + halfSize);
                hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);


                halfSize = new Vector2(width, Thickness) * .5f;

                pos = Position + new Vector2(0f, (-height + Thickness) * .5f);

                box.bounds = new BoundingBox2(pos - halfSize, pos + halfSize);
                hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);
            }
        }
    }
}
