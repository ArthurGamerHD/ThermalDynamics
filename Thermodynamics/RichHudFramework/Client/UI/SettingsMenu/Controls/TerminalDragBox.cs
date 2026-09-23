using VRageMath;

namespace RichHudFramework.UI.Client
{
	public enum DragBoxAccessors : int
	{
		BoxSize = 16,
		AlignToEdge = 17,
	}

	public class TerminalDragBox : TerminalValue<Vector2>
	{
		public Vector2 BoxSize
		{

			get { return (Vector2)GetOrSetMember(null, (int)DragBoxAccessors.BoxSize); }

			set { GetOrSetMember(value, (int)DragBoxAccessors.BoxSize); }
		}

		public bool AlignToEdge
		{

			get { return (bool)GetOrSetMember(null, (int)DragBoxAccessors.AlignToEdge); }

			set { GetOrSetMember(value, (int)DragBoxAccessors.AlignToEdge); }
		}


		public TerminalDragBox() : base(MenuControls.DragBox)
		{ }
	}
}