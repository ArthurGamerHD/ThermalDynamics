using System;
using System.Collections.Generic;
using VRage;
using VRage.Input;

namespace RichHudFramework
{
	using KeyComboInitData = IReadOnlyList<int>;

	namespace UI
	{
		using Server;
		using Client;
		using System.Collections;
		using BindInitData = MyTuple<string, KeyComboInitData, IReadOnlyList<KeyComboInitData>>;

		public class BindGroupInitializer : IReadOnlyList<BindInitData>
		{
			public BindInitData this[int index] => bindData[index];

			public int Count => bindData.Count;

			private readonly List<BindInitData> bindData;

/// <summary>BindGroupInitializer operation.</summary>
			public BindGroupInitializer()
			{
/// <summary>List operation.</summary>
				bindData = new List<BindInitData>();
			}

/// <summary>Returns the enumerator.</summary>
			public IEnumerator<BindInitData> GetEnumerator() =>
				bindData.GetEnumerator();

			IEnumerator IEnumerable.GetEnumerator() =>
				bindData.GetEnumerator();

/// <summary>Adds a .</summary>
			public void Add(string bindName, ControlHandle? con1 = null, ControlHandle? con2 = null, ControlHandle? con3 = null)
			{
/// <summary>KeyComboInit operation.</summary>
				var combo = new KeyComboInit();

				if (con1 != null)
					combo.Add(con1.Value);

				if (con2 != null)
					combo.Add(con2.Value);

				if (con3 != null)
					combo.Add(con3.Value);

				bindData.Add(new BindInitData(bindName, combo, null));
			}

/// <summary>Adds a .</summary>
			public void Add(string bindName, ControlHandle? con1, KeyComboInit alias)
			{
/// <summary>KeyComboInit operation.</summary>
				var combo = new KeyComboInit();

				if (con1 != null)
					combo.Add(con1.Value);

				bindData.Add(new BindInitData(bindName, combo, new List<KeyComboInitData> { alias }));
			}

/// <summary>Adds a .</summary>
			public void Add(string bindName, ControlHandle? con1, ControlHandle? con2, KeyComboInit alias)
			{
/// <summary>KeyComboInit operation.</summary>
				var combo = new KeyComboInit();

				if (con1 != null)
					combo.Add(con1.Value);

				if (con2 != null)
					combo.Add(con2.Value);

				bindData.Add(new BindInitData(bindName, combo, new List<KeyComboInitData> { alias }));
			}

/// <summary>Adds a .</summary>
			public void Add(string bindName, ControlHandle? con1, ControlHandle? con2, ControlHandle? con3, KeyComboInit alias)
			{
/// <summary>KeyComboInit operation.</summary>
				var combo = new KeyComboInit();

				if (con1 != null)
					combo.Add(con1.Value);

				if (con2 != null)
					combo.Add(con2.Value);

				if (con3 != null)
					combo.Add(con3.Value);

				bindData.Add(new BindInitData(bindName, combo, new List<KeyComboInitData> { alias }));
			}

/// <summary>Adds a .</summary>
			public void Add(string bindName, KeyComboInit combo, KeyComboInit alias)
			{
				bindData.Add(new BindInitData(bindName, combo, new List<KeyComboInitData> { alias }));
			}

/// <summary>Adds a .</summary>
			public void Add(string bindName, KeyComboInit combo, KeyComboInit alias1, KeyComboInit alias2)
			{
				bindData.Add(new BindInitData(bindName, combo, new List<KeyComboInitData> { alias1, alias2 }));
			}

/// <summary>Returns the binddefinitions.</summary>
			public BindDefinition[] GetBindDefinitions()
			{
				var bindDefs = new BindDefinition[bindData.Count];

				for (int i = 0; i < bindData.Count; i++)
				{
					var bindName = bindData[i].Item1;
					var mainCombo = bindData[i].Item2;
					var aliases = bindData[i].Item3;

					bindDefs[i].name = bindName;

					if (mainCombo != null)
						bindDefs[i].controlNames = BindManager.GetControlNames(mainCombo);

					if (aliases != null)
					{
						bindDefs[i].aliases = new BindAliasDefinition[aliases.Count];

						for (int j = 0; j < aliases.Count; j++)
							bindDefs[i].aliases[j].controlNames = BindManager.GetControlNames(aliases[j]);
					}
				}

				return bindDefs;
			}

/// <summary>List operation.</summary>
			public static implicit operator List<BindInitData>(BindGroupInitializer gInit)
			{
				return gInit.bindData;
			}
		}

		public class KeyComboInit : IReadOnlyList<int>
		{
			public int this[int index] => comboData[index];

			public int Count => comboData.Count;

			private readonly List<int> comboData;

/// <summary>KeyComboInit operation.</summary>
			public KeyComboInit()
			{
/// <summary>List operation.</summary>
				comboData = new List<int>(3);
			}

/// <summary>KeyComboInit operation.</summary>
			public KeyComboInit(List<int> comboData)
			{
				this.comboData = comboData;
			}

/// <summary>KeyComboInit operation.</summary>
			public KeyComboInit(ControlHandle con)
			{
				comboData = new List<int> { con.id };
			}

/// <summary>KeyComboInit operation.</summary>
			public KeyComboInit(ControlHandle con1, ControlHandle con2)
			{
				comboData = new List<int> { con1.id, con2.id };
			}

/// <summary>KeyComboInit operation.</summary>
			public KeyComboInit(ControlHandle con1, ControlHandle con2, ControlHandle con3)
			{
				comboData = new List<int> { con1.id, con2.id, con3.id };
			}

/// <summary>Returns the enumerator.</summary>
			public IEnumerator<int> GetEnumerator() =>
				comboData.GetEnumerator();

			IEnumerator IEnumerable.GetEnumerator() =>
				comboData.GetEnumerator();

/// <summary>Adds a .</summary>
			public void Add(ControlHandle con)
			{
				if (comboData.Count < BindManager.MaxBindLength)
					comboData.Add(con.id);
				else
					throw new Exception("Attempted to add more than 3 controls to a key combo.");
			}

/// <summary>KeyComboInit operation.</summary>
			public static implicit operator KeyComboInit(List<int> comboData)
			{
				return new KeyComboInit(comboData);
			}

/// <summary>List operation.</summary>
			public static implicit operator List<int>(KeyComboInit cInit)
			{
				return cInit.comboData;
			}
		}
	}
}