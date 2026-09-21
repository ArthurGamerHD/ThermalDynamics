namespace RichHudFramework.UI
{
	public class SelectionBoxEntry<TElement> : HudElementContainer<TElement>, ISelectionBoxEntry<TElement>
		where TElement : HudElementBase
	{
		public virtual bool Enabled { get; set; }

		public virtual bool AllowHighlighting { get; set; }

/// <summary>SelectionBoxEntry operation.</summary>
		public SelectionBoxEntry()
		{
			Enabled = true;
			AllowHighlighting = true;
		}

/// <summary>Reset operation.</summary>
		public virtual void Reset()
		{
			Enabled = true;
			AllowHighlighting = true;
		}
	}

	public class SelectionBoxEntryTuple<TElement, TValue>
		: SelectionBoxEntry<TElement>, ISelectionBoxEntryTuple<TElement, TValue>
		where TElement : HudElementBase
	{
		public TValue AssocMember { get; set; }

/// <summary>Reset operation.</summary>
		public override void Reset()
		{
			Enabled = true;
			AllowHighlighting = true;
/// <summary>default operation.</summary>
			AssocMember = default(TValue);
		}
	}
}
