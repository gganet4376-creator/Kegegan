using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kagegan
{
    /// <summary>Simple stack pool. Objects are deactivated on release and reactivated on Get.</summary>
    public class Pool<T> where T : Component
    {
        readonly Stack<T> free = new Stack<T>();
        readonly Func<T> factory;
        public readonly List<T> Active = new List<T>();

        public Pool(Func<T> factory, int prewarm = 0)
        {
            this.factory = factory;
            for (int i = 0; i < prewarm; i++)
            {
                var t = factory();
                t.gameObject.SetActive(false);
                free.Push(t);
            }
        }

        public T Get()
        {
            T t = null;
            while (free.Count > 0 && t == null) t = free.Pop();
            if (t == null) t = factory();
            t.gameObject.SetActive(true);
            Active.Add(t);
            return t;
        }

        public void Release(T t)
        {
            if (t == null) return;
            int i = Active.IndexOf(t);
            if (i >= 0) { Active[i] = Active[Active.Count - 1]; Active.RemoveAt(Active.Count - 1); }
            t.gameObject.SetActive(false);
            free.Push(t);
        }

        public void ReleaseAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var t = Active[i];
                if (t != null) { t.gameObject.SetActive(false); free.Push(t); }
            }
            Active.Clear();
        }
    }
}
