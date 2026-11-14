using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.MirObjects.Monsters
{
    public class Jar1 : MonsterObject
    {
        protected virtual byte AttackRange
        {
            get
            {
                return 1;
            }
        }

        protected override bool CanMove { get { return false; } }
        protected override bool CanRegen { get { return false; } }

        protected override bool InAttackRange()
        {
            return CurrentMap == Target.CurrentMap && Functions.InRange(CurrentLocation, Target.CurrentLocation, AttackRange);
        }

        protected internal Jar1(MonsterInfo info)
            : base(info)
        {

        }

        public override void Die()
        {
            ActionList.Add(new DelayedAction(DelayedType.Die, Envir.Time + 1000));

            base.Die();
        }

        protected override void CompleteDeath(IList<object> data)
        {
            SpawnSlave();
        }

        private void SpawnSlave()
        {
            ActionTime = Envir.Time + 300;
            AttackTime = Envir.Time + AttackSpeed;

            // 体检遗留项：原 Where 惰性链在 Count()/Count()/ElementAt() 处各枚举全表一次（共 3 遍 + 迭代器/闭包分配）；
            // 改为单遍 for 收集，仍逐条比较同一等级窗口，元素顺序与随机分布完全等价
            var monsters = new List<MonsterInfo>();
            var infoList = Envir.MonsterInfoList;
            for (int i = 0; i < infoList.Count; i++)
            {
                MonsterInfo info = infoList[i];
                if (info.Level <= Level && info.Level >= (Level - 10))
                    monsters.Add(info);
            }

            if (monsters.Count > 0)
            {
                var monster = monsters[Envir.Random.Next(monsters.Count)];

                var mob = GetMonster(monster);

                if (mob == null) return;

                mob.Spawn(CurrentMap, CurrentLocation);

                mob.Target = Target;
                mob.ActionTime = Envir.Time + 2000;
            }
        }
    }
}
