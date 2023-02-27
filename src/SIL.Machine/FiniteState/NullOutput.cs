﻿using System;
using SIL.Machine.Annotations;

namespace SIL.Machine.FiniteState
{
    public class NullOutput<TData, TOffset> : Output<TData, TOffset>, IEquatable<NullOutput<TData, TOffset>>
        where TData : IAnnotatedData<TOffset>
    {
        internal NullOutput()
            : base(null) { }

        public override Annotation<TOffset> UpdateOutput(
            TData data,
            Annotation<TOffset> ann,
            IFstOperations<TData, TOffset> operations
        )
        {
            return null;
        }

        public override bool Equals(object obj)
        {
            return obj != null && Equals(obj as NullOutput<TData, TOffset>);
        }

        public bool Equals(NullOutput<TData, TOffset> other)
        {
            return other != null;
        }

        public override int GetHashCode()
        {
            return 2;
        }

        public override string ToString()
        {
            return "∅";
        }
    }
}
