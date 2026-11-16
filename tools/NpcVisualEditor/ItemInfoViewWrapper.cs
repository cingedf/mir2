using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace NpcVisualEditor
{
    /// <summary>
    /// stdmode 动态字段名：按 ItemType 暴露属性子集 + 中文 DisplayName。
    /// 对标社区工具的"物品按类型动态字段名"，本血统无 StdMode，等价 ItemType:byte。
    /// 不改原 ItemInfo，仅包装供 PropertyGrid 展示（只读）。
    /// </summary>
    public class ItemInfoViewWrapper : CustomTypeDescriptor
    {
        private readonly ItemInfo _item;

        public ItemInfoViewWrapper(ItemInfo item) { _item = item; }

        public override PropertyDescriptorCollection GetProperties()
        {
            return GetProperties(null);
        }

        public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            var list = new List<PropertyDescriptor>();
            void Add(string name, string cn, Type type, Func<object> get)
                => list.Add(new DynamicProp(name, cn, type, get));

            // 通用字段（所有类型）
            Add("Index", "编号", typeof(int), () => _item.Index);
            Add("Name", "名称", typeof(string), () => _item.Name);
            Add("Type", "物品类型", typeof(ItemType), () => _item.Type);
            Add("Image", "外观编号", typeof(ushort), () => _item.Image);
            Add("Durability", "持久", typeof(ushort), () => _item.Durability);
            Add("Price", "价格", typeof(uint), () => _item.Price);
            Add("Weight", "重量", typeof(byte), () => _item.Weight);
            Add("Light", "亮度", typeof(byte), () => _item.Light);
            Add("StackSize", "堆叠上限", typeof(ushort), () => _item.StackSize);
            Add("RequiredAmount", "需求等级", typeof(byte), () => _item.RequiredAmount);
            Add("Slots", "孔数", typeof(byte), () => _item.Slots);
            Add("Shape", "形状", typeof(short), () => _item.Shape);
            Add("Grade", "品级", typeof(ItemGrade), () => _item.Grade);
            Add("RequiredClass", "需求职业", typeof(RequiredClass), () => _item.RequiredClass);
            Add("RequiredGender", "需求性别", typeof(RequiredGender), () => _item.RequiredGender);

            // 按类型强调相关属性标签（动态字段名核心）
            switch (_item.Type)
            {
                case ItemType.武器:
                    Add("DC", "攻击", typeof(string), () => Range(Stat.MinDC, Stat.MaxDC));
                    Add("ACC", "准确", typeof(int), () => GetStat(Stat.准确));
                    break;
                case ItemType.盔甲:
                    Add("AC", "防御", typeof(string), () => Range(Stat.MinAC, Stat.MaxAC));
                    Add("MAC", "魔防", typeof(string), () => Range(Stat.MinMAC, Stat.MaxMAC));
                    break;
                case ItemType.项链:
                case ItemType.戒指:
                case ItemType.手镯:
                    Add("SC", "道术", typeof(string), () => Range(Stat.MinSC, Stat.MaxSC));
                    Add("DC", "攻击", typeof(string), () => Range(Stat.MinDC, Stat.MaxDC));
                    break;
            }

            // Stats 中所有非零项，按枚举成员名 + 中文标签
            if (_item.Stats != null)
                foreach (var kv in _item.Stats.Values.Where(x => x.Value != 0))
                    Add("Stat_" + kv.Key, StatLabel(kv.Key), typeof(int), () => kv.Value);

            return new PropertyDescriptorCollection(list.ToArray());
        }

        private int GetStat(Stat s) => _item.Stats != null ? _item.Stats[s] : 0;

        private string Range(Stat lo, Stat hi) => GetStat(lo) + "-" + GetStat(hi);

        private static string StatLabel(Stat s)
        {
            switch (s)
            {
                case Stat.MinAC: return "下限防御";
                case Stat.MaxAC: return "上限防御";
                case Stat.MinMAC: return "下限魔防";
                case Stat.MaxMAC: return "上限魔防";
                case Stat.MinDC: return "下限攻击";
                case Stat.MaxDC: return "上限攻击";
                case Stat.MinMC: return "下限魔法";
                case Stat.MaxMC: return "上限魔法";
                case Stat.MinSC: return "下限道术";
                case Stat.MaxSC: return "上限道术";
                case Stat.准确: return "准确";
                case Stat.敏捷: return "敏捷";
                case Stat.HP: return "生命";
                case Stat.MP: return "魔法值";
                case Stat.攻击速度: return "攻击速度";
                case Stat.幸运: return "幸运";
                case Stat.暴击率: return "暴击率";
                case Stat.暴击伤害: return "暴击伤害";
                default: return s.ToString();
            }
        }

        private class DynamicProp : PropertyDescriptor
        {
            private readonly Type _type;
            private readonly Func<object> _get;

            public DynamicProp(string name, string display, Type type, Func<object> get)
                : base(name, new Attribute[] { new DisplayNameAttribute(display) })
            {
                _type = type; _get = get;
            }

            public override Type ComponentType => typeof(ItemInfo);
            public override bool IsReadOnly => true;
            public override Type PropertyType => _type;
            public override object GetValue(object component) => _get();
            public override void SetValue(object component, object value) { }
            public override bool CanResetValue(object component) => false;
            public override void ResetValue(object component) { }
            public override bool ShouldSerializeValue(object component) => false;
        }
    }
}
