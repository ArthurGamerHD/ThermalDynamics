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
/// <summary>return operation.</summary>
			get { return (Vector2)GetOrSetMember(null, (int)DragBoxAccessors.BoxSize); }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)DragBoxAccessors.BoxSize); }
		}

		public bool AlignToEdge
		{
/// <summary>return operation.</summary>
			get { return (bool)GetOrSetMember(null, (int)DragBoxAccessors.AlignToEdge); }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)DragBoxAccessors.AlignToEdge); }
		}

/// <summary>TerminalDragBox operation.</summary>
		public TerminalDragBox() : base(MenuControls.DragBox)
		{ }
	}
}