using System.Collections.Generic;
using System;
using VRage;
using VRageMath;

namespace RichHudFramework
{
    using BindDefinitionData = MyTuple<string, string[], string[][]>;

    namespace UI.Client
    {
        public sealed partial class BindManager
        {
            private partial class BindGroup : ReadOnlyApiCollection<IBind>, IBindGroup
            {
                public IBind this[string name] 
                { 
                    get 
                    {

                        IBind bind = GetBind(name);

                        if (bind == null)
                            throw new Exception($"Bind: {name} was not found in bind group {Name}.");
                        else
                            return bind;
                    } 
                }

                public string Name => _instance.GetOrSetGroupMemberFunc(Index, null, (int)BindGroupAccessors.Name) as string;

                public int Index { get; }

                public object ID => _instance.GetOrSetGroupMemberFunc(Index, null, (int)BindGroupAccessors.ID);


                public BindGroup(int index) 
                    : base(x => new Bind(new Vector2I(index, x)), () => _instance.GetBindCountFunc(index))
                {
                    Index = index;
                }


                public bool DoesBindExist(string name) =>
                    (bool)_instance.GetOrSetGroupMemberFunc(Index, name, (int)BindGroupAccessors.DoesBindExist);


                public bool DoesComboConflict(IReadOnlyList<ControlHandle> newCombo, IBind currentBind = null, int alias = 0)
                {
                    var data = new MyTuple<IReadOnlyList<int>, int, int>(GetComboIndicesTemp(newCombo), currentBind?.Index ?? -1, alias);
                    return (bool)_instance.GetOrSetGroupMemberFunc(Index, data, (int)BindGroupAccessors.DoesComboConflict);
                }


                public bool DoesComboConflict(IReadOnlyList<int> newCombo, IBind currentBind = null, int alias = 0)
                {
                    var data = new MyTuple<IReadOnlyList<int>, int, int>(newCombo, currentBind?.Index ?? -1, alias);
                    return (bool)_instance.GetOrSetGroupMemberFunc(Index, data, (int)BindGroupAccessors.DoesComboConflict);
                }


                public bool TryLoadBindData(IReadOnlyList<BindDefinitionData> bindData) =>
                    (bool)_instance.GetOrSetGroupMemberFunc(Index, bindData, (int)BindGroupAccessors.TryLoadBindData);


                public bool TryLoadBindData(IReadOnlyList<BindDefinition> bindData)
                {
                    var defData = new BindDefinitionData[bindData.Count];

                    for (int i = 0; i < bindData.Count; i++)
                    {
                        string[][] aliasData = null;

                        if (bindData[i].aliases != null)
                        {
                            aliasData = new string[bindData[i].aliases.Length][];

                            for (int j = 0; j < aliasData.Length; j++)
                                aliasData[j] = bindData[i].aliases[j];
                        }


                        defData[i] = new BindDefinitionData(bindData[i].name, bindData[i].controlNames, aliasData);
                    }

                    return (bool)_instance.GetOrSetGroupMemberFunc(Index, defData, (int)BindGroupAccessors.TryLoadBindData);
                }


                public void RegisterBinds(BindGroupInitializer bindData)
                {
                    foreach (var bind in bindData)
                        _instance.GetOrSetGroupMemberFunc(Index, bind, (int)BindGroupAccessors.AddBindWithIndices);
                }


                public void RegisterBinds(IReadOnlyList<string> bindNames) =>
                    _instance.GetOrSetGroupMemberFunc(Index, bindNames, (int)BindGroupAccessors.RegisterBindNames);


                public IBind GetBind(string name)
                {
                    var index = (Vector2I)_instance.GetOrSetGroupMemberFunc(Index, name, (int)BindGroupAccessors.GetBindFromName);
                    return index.Y != -1 ? this[index.Y] : null;
                }


                public IBind AddBind(string bindName, IReadOnlyList<int> newConIDs, IReadOnlyList<IReadOnlyList<int>> aliases = null)
                {
                    var bindData = new MyTuple<string, IReadOnlyList<int>, IReadOnlyList<IReadOnlyList<int>>>();
                    bindData.Item1 = bindName;
                    bindData.Item2 = newConIDs;
                    bindData.Item3 = aliases;

                    var index = (Vector2I)_instance.GetOrSetGroupMemberFunc(Index, bindData, (int)BindGroupAccessors.AddBindWithIndices);                        
                    return this[index.Y];
                }


                public IBind AddBind(string bindName, IReadOnlyList<ControlHandle> combo, IReadOnlyList<IReadOnlyList<ControlHandle>> aliases = null)
                {
                    var bindData = new MyTuple<string, IReadOnlyList<int>, IReadOnlyList<IReadOnlyList<int>>>();
                    bindData.Item1 = bindName;

                    bindData.Item2 = GetComboIndicesTemp(combo);

                    var aliasData = (aliases.Count > 0) ? new List<int>[aliases.Count] : null;
                    bindData.Item3 = aliasData;

                    if (aliases.Count > 0)
                    {
                        for (int i = 0; i < aliases.Count; i++)
                        {
                            var alias = aliases[i];

                            aliasData[i] = new List<int>();
                            GetComboIndices(alias, aliasData[i]);
                        }
                    }

                    var index = (Vector2I)_instance.GetOrSetGroupMemberFunc(Index, bindData, (int)BindGroupAccessors.AddBindWithIndices);
                    return this[index.Y];
                }


                public bool TryRegisterBind(string bindName, out IBind newBind)
                {
                    int index = (int)_instance.GetOrSetGroupMemberFunc(Index, bindName, (int)BindGroupAccessors.TryRegisterBindName);

                    if (index != -1)
                    {
                        newBind = this[index];
                        return true;
                    }
                    else
                    {
                        newBind = null;
                        return false;
                    }
                }


                public bool TryRegisterBind(string bindName, out IBind newBind, IReadOnlyList<int> combo, IReadOnlyList<IReadOnlyList<int>> aliases = null)
                {
                    var bindData = new MyTuple<string, IReadOnlyList<int>, IReadOnlyList<IReadOnlyList<int>>>();
                    bindData.Item1 = bindName;
                    bindData.Item2 = combo;
                    bindData.Item3 = aliases;

                    int index = (int)_instance.GetOrSetGroupMemberFunc(Index, bindData, (int)BindGroupAccessors.TryRegisterBindWithIndices);

                    if (index != -1)
                    {
                        newBind = this[index];
                        return true;
                    }
                    else
                    {
                        newBind = null;
                        return false;
                    }
                }


                public bool TryRegisterBind(string bindName, out IBind newBind, IReadOnlyList<ControlHandle> combo, IReadOnlyList<IReadOnlyList<ControlHandle>> aliases = null)
                {
                    var bindData = new MyTuple<string, IReadOnlyList<int>, IReadOnlyList<IReadOnlyList<int>>>();
                    bindData.Item1 = bindName;

                    bindData.Item2 = GetComboIndicesTemp(combo);

                    var aliasData = (aliases.Count > 0) ? new List<int>[aliases.Count] : null;
                    bindData.Item3 = aliasData;

                    int index = (int)_instance.GetOrSetGroupMemberFunc(Index, bindData, (int)BindGroupAccessors.TryRegisterBindWithIndices);

                    if (index != -1)
                    {
                        newBind = this[index];
                        return true;
                    }
                    else
                    {
                        newBind = null;
                        return false;
                    }
                }


                public BindDefinition[] GetBindDefinitions()
                {
                    var bindData = _instance.GetOrSetGroupMemberFunc(Index, null, (int)BindGroupAccessors.GetBindData) as BindDefinitionData[];
                    var definitions = new BindDefinition[bindData.Length];

                    for (int n = 0; n < bindData.Length; n++)
                        definitions[n] = (BindDefinition)bindData[n];

                    return definitions;
                }


                public BindDefinitionData[] GetBindData() =>
                    _instance.GetOrSetGroupMemberFunc(Index, null, (int)BindGroupAccessors.GetBindData) as BindDefinitionData[];


                public void ClearSubscribers() =>
                    _instance.GetOrSetGroupMemberFunc(Index, null, (int)BindGroupAccessors.ClearSubscribers);


                public override bool Equals(object obj)
                {
                    return Index.Equals(obj);
                }


                public override int GetHashCode()
                {
                    return Index.GetHashCode();
                }
            }
        }
    }
}