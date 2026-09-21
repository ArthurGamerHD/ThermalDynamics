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

/// <summary>BorderBox operation.</summary>
		public BorderBox(HudParentBase parent) : base(parent)
        {
/// <summary>MatBoard operation.</summary>
            hudBoard = new MatBoard();
            Thickness = 1f;
        }

/// <summary>BorderBox operation.</summary>
        public BorderBox() : this(null)
        { }

/// <summary>Draw operation.</summary>
        protected override void Draw()
        {
            if (Color.A > 0)
            {
/// <summary>default operation.</summary>
                CroppedBox box = default(CroppedBox);
                box.mask = MaskingBox;

                float height = UnpaddedSize.Y, 
                    width = UnpaddedSize.X;
                Vector2 halfSize, pos;

/// <summary>Vector2 operation.</summary>
                halfSize = new Vector2(Thickness, height) * .5f;
/// <summary>Vector2 operation.</summary>
                pos = Position + new Vector2((-width + Thickness) * .5f, 0f);
/// <summary>BoundingBox2 operation.</summary>
                box.bounds = new BoundingBox2(pos - halfSize, pos + halfSize);
                hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);

/// <summary>Vector2 operation.</summary>
                halfSize = new Vector2(width, Thickness) * .5f;
/// <summary>Vector2 operation.</summary>
                pos = Position + new Vector2(0f, (height - Thickness) * .5f);
/// <summary>BoundingBox2 operation.</summary>
                box.bounds = new BoundingBox2(pos - halfSize, pos + halfSize);
                hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);

/// <summary>Vector2 operation.</summary>
                halfSize = new Vector2(Thickness, height) * .5f;
/// <summary>Vector2 operation.</summary>
                pos = Position + new Vector2((width - Thickness) * .5f, 0f);
/// <summary>BoundingBox2 operation.</summary>
                box.bounds = new BoundingBox2(pos - halfSize, pos + halfSize);
                hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);

/// <summary>Vector2 operation.</summary>
                halfSize = new Vector2(width, Thickness) * .5f;
/// <summary>Vector2 operation.</summary>
                pos = Position + new Vector2(0f, (-height + Thickness) * .5f);
/// <summary>BoundingBox2 operation.</summary>
                box.bounds = new BoundingBox2(pos - halfSize, pos + halfSize);
                hudBoard.Draw(ref box, HudSpace.PlaneToWorldRef);
            }
        }
    }
}
