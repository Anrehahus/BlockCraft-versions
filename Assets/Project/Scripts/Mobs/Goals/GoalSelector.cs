using System.Collections.Generic;

namespace MinecraftClone.Mobs
{
    public class GoalSelector
    {
        private class Entry { public int Priority; public Goal Goal; public bool Running; }

        private readonly List<Entry> _entries = new List<Entry>();

        public void Add(int priority, Goal goal)
        {
            _entries.Add(new Entry { Priority = priority, Goal = goal });
            _entries.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public void Tick(float dt)
        {
            // 1. останавливаем те, что больше не могут работать
            foreach (var e in _entries)
                if (e.Running && !e.Goal.CanContinue()) { e.Goal.Stop(); e.Running = false; }

            // 2. запускаем новые (от важных к менее важным)
            foreach (var e in _entries)
            {
                if (e.Running) continue;
                if (!CanTakeOver(e)) continue;
                if (!e.Goal.CanStart()) continue;

                foreach (var o in _entries)
                    if (o.Running && (o.Goal.Flags & e.Goal.Flags) != 0) { o.Goal.Stop(); o.Running = false; }

                e.Goal.Start();
                e.Running = true;
            }

            // 3. работаем
            foreach (var e in _entries)
                if (e.Running) e.Goal.Tick(dt);
        }

        // нельзя запускаться, если более важное (или равное) поведение уже занимает те же флаги
        private bool CanTakeOver(Entry e)
        {
            foreach (var o in _entries)
                if (o.Running && o.Priority <= e.Priority && (o.Goal.Flags & e.Goal.Flags) != 0) return false;
            return true;
        }
    }
}