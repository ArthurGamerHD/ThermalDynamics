using System;
using System.Collections.Generic;
using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		using static RichHudFramework.UI.NodeConfigIndices;

		public abstract partial class HudNodeBase
		{
			protected static class NodeUtils
			{
/// <summary>Registers the API and message handler.</summary>
				public static void RegisterNodes(HudParentBase newParent, IReadOnlyList<HudNodeBase> nodes)
				{
					ParentUtils.RegisterNodes(newParent, nodes);

					for (int n = 0; n < nodes.Count; n++)
					{
						HudNodeBase node = nodes[n];
						node.Parent = newParent;

						node._config[StateID] |= (uint)HudElementStates.IsRegistered;
						node._config[StateID] &= ~(uint)HudElementStates.WasParentVisible;
					}
				}

				public static void RegisterNodes<TCon, TNode>(HudParentBase newParent, IReadOnlyList<TCon> nodes)
/// <summary>new operation.</summary>
					where TCon : IHudNodeContainer<TNode>, new()
					where TNode : HudNodeBase
				{
					ParentUtils.RegisterNodes<TCon, TNode>(newParent, nodes);

					for (int n = 0; n < nodes.Count; n++)
					{
						HudNodeBase node = nodes[n].Element;
						node.Parent = newParent;

						node._config[StateID] |= (uint)HudElementStates.IsRegistered;
						node._config[StateID] &= ~(uint)HudElementStates.WasParentVisible;
					}
				}

/// <summary>Unregisters the API and cleans resources.</summary>
				public static void UnregisterNodes(HudParentBase parent, IReadOnlyList<HudNodeBase> nodes, int index, int count)
				{
					if (count > 0)
					{
						ParentUtils.UnregisterNodes(parent, nodes, index, count);

						for (int n = index; n < count; n++)
						{
							HudNodeBase node = nodes[n];
							HudParentBase nodeParent = node.Parent;

							if (nodeParent != parent)
								throw new Exception("The child node specified is not registered to the parent given.");

							node.Parent = null;
							node._dataHandle[0].Item4 = null;
							node._config[StateID] &= (uint)~(HudElementStates.IsRegistered | HudElementStates.WasParentVisible);
						}
					}
				}

				public static void UnregisterNodes<TCon, TNode>(HudParentBase parent, IReadOnlyList<TCon> nodes, int index, int count)
/// <summary>new operation.</summary>
					where TCon : IHudNodeContainer<TNode>, new()
					where TNode : HudNodeBase
				{
					if (count > 0)
					{
						ParentUtils.UnregisterNodes<TCon, TNode>(parent, nodes, index, count);

						for (int n = index; n < count; n++)
						{
							HudNodeBase node = nodes[n].Element;
							HudParentBase nodeParent = node.Parent;

							if (nodeParent != parent)
								throw new Exception("The child node specified is not registered to the parent given.");

							node.Parent = null;
							node._dataHandle[0].Item4 = null;
							node._config[StateID] &= (uint)~(HudElementStates.IsRegistered | HudElementStates.WasParentVisible);
						}
					}
				}

/// <summary>Sets the nodesstate.</summary>
				public static void SetNodesState(HudElementStates state, bool mask, IReadOnlyList<HudNodeBase> nodes, int index, int count)
				{
					if (count > 0)
					{
						int end = index + count - 1;
						Utils.Debug.Assert(index >= 0 && end < nodes.Count, $"Range out of bounds. Index: {index}, End: {end}");

						if (mask)
						{
							for (int i = index; i <= end; i++)
							{
								nodes[i]._config[StateID] &= (uint)~state;
							}
						}
						else
						{
							for (int i = index; i <= end; i++)
							{
								nodes[i]._config[StateID] |= (uint)state;
							}
						}
					}
				}

				public static void SetNodesState<TCon, TNode>(HudElementStates state, bool mask, IReadOnlyList<TCon> nodes, int index, int count)
/// <summary>new operation.</summary>
					where TCon : IHudNodeContainer<TNode>, new()
					where TNode : HudNodeBase
				{
					if (count > 0)
					{
						int end = index + count - 1;
						Utils.Debug.Assert(index >= 0 && end < nodes.Count, $"Range out of bounds. Index: {index}, End: {end}");

						if (mask)
						{
							for (int i = index; i <= end; i++)
							{
								nodes[i].Element._config[StateID] &= (uint)~state;
							}
						}
						else
						{
							for (int i = index; i <= end; i++)
							{
								nodes[i].Element._config[StateID] |= (uint)state;
							}
						}
					}
				}
			}
		}
		
		public abstract partial class HudElementBase
		{
			public static class ElementUtils
			{
/// <summary>UpdateRootAnchoring operation.</summary>
				public static void UpdateRootAnchoring(Vector2 size, IReadOnlyList<HudNodeBase> children)
				{
					for (int i = 0; i < children.Count; i++)
					{
						var child = children[i] as HudElementBase;

						if (child != null && (child.Config[StateID] & (child.Config[VisMaskID])) == child.Config[VisMaskID])
						{
							ParentAlignments originFlags = child.ParentAlignment;
							Vector2 delta = Vector2.Zero,
								childSize = child.UnpaddedSize + child.Padding,
								max = (size - childSize) * .5f,
								min = -max;

							if ((originFlags & ParentAlignments.Bottom) == ParentAlignments.Bottom)
								delta.Y = min.Y;
							else if ((originFlags & ParentAlignments.Top) == ParentAlignments.Top)
								delta.Y = max.Y;

							if ((originFlags & ParentAlignments.Left) == ParentAlignments.Left)
								delta.X = min.X;
							else if ((originFlags & ParentAlignments.Right) == ParentAlignments.Right)
								delta.X = max.X;

							child.Origin = delta;
						}
					}
				}
			}
		}
	}
}