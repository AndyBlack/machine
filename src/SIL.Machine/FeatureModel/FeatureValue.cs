using System.Collections.Generic;
using SIL.ObjectModel;

namespace SIL.Machine.FeatureModel
{
    public abstract class FeatureValue : ICloneable<FeatureValue>
    {
        internal FeatureValue Forward { get; set; }

        internal abstract bool UnionImpl(
            FeatureValue other,
            VariableBindings varBindings,
            IDictionary<FeatureStruct, ISet<FeatureStruct>> visited
        );
        internal abstract bool AddImpl(
            FeatureValue other,
            VariableBindings varBindings,
            IDictionary<FeatureStruct, ISet<FeatureStruct>> visited
        );
        internal abstract bool SubtractImpl(
            FeatureValue other,
            VariableBindings varBindings,
            IDictionary<FeatureStruct, ISet<FeatureStruct>> visited
        );
        internal abstract FeatureValue CloneImpl(IDictionary<FeatureValue, FeatureValue> copies);
        internal abstract bool ValueEqualsImpl(
            FeatureValue other,
            ISet<FeatureValue> visitedSelf,
            ISet<FeatureValue> visitedOther,
            IDictionary<FeatureValue, FeatureValue> visitedPairs
        );
        internal abstract int FreezeImpl(ISet<FeatureValue> visited);
        internal abstract string ToStringImpl(ISet<FeatureValue> visited, IDictionary<FeatureValue, int> reentranceIds);

        internal abstract bool IsUnifiableImpl(FeatureValue other, bool useDefaults, VariableBindings varBindings);
        internal abstract bool SubsumesImpl(FeatureValue other, bool useDefaults, VariableBindings varBindings);
        internal abstract bool DestructiveUnify(
            FeatureValue other,
            bool useDefaults,
            bool preserveInput,
            IDictionary<FeatureValue, FeatureValue> copies,
            VariableBindings varBindings
        );
        protected abstract bool NondestructiveUnify(
            FeatureValue other,
            bool useDefaults,
            IDictionary<FeatureValue, FeatureValue> copies,
            VariableBindings varBindings,
            out FeatureValue output
        );
        internal abstract void FindReentrances(IDictionary<FeatureValue, bool> reentrances);

        internal bool UnifyImpl(
            FeatureValue other,
            bool useDefaults,
            VariableBindings varBindings,
            out FeatureValue output
        )
        {
            var copies = new Dictionary<FeatureValue, FeatureValue>();
            return UnifyImpl(other, useDefaults, copies, varBindings, out output);
        }

        internal bool UnifyImpl(
            FeatureValue other,
            bool useDefaults,
            IDictionary<FeatureValue, FeatureValue> copies,
            VariableBindings varBindings,
            out FeatureValue output
        )
        {
            other = Dereference(other);

            FeatureValue fv1;
            if (!copies.TryGetValue(this, out fv1))
                fv1 = null;
            FeatureValue fv2;
            if (!copies.TryGetValue(other, out fv2))
                fv2 = null;

            if (fv1 == null && fv2 == null)
            {
                if (!NondestructiveUnify(other, useDefaults, copies, varBindings, out output))
                {
                    output = null;
                    return false;
                }
            }
            else if (fv1 != null && fv2 != null)
            {
                if (!fv1.DestructiveUnify(fv2, useDefaults, false, copies, varBindings))
                {
                    output = null;
                    return false;
                }
                output = fv1;
            }
            else if (fv1 != null)
            {
                if (!fv1.DestructiveUnify(other, useDefaults, true, copies, varBindings))
                {
                    output = null;
                    return false;
                }
                output = fv1;
            }
            else
            {
                if (!fv2.DestructiveUnify(this, useDefaults, true, copies, varBindings))
                {
                    output = null;
                    return false;
                }
                output = fv2;
            }

            return true;
        }

        protected static bool Dereference<T>(FeatureValue value, out T actualValue)
            where T : FeatureValue
        {
            value = Dereference(value);

            actualValue = value as T;
            return actualValue != null;
        }

        protected static T Dereference<T>(T value)
            where T : FeatureValue
        {
            FeatureValue fv = value;
            while (fv.Forward != null)
                fv = fv.Forward;
            return (T)fv;
        }

        public abstract bool ValueEquals(FeatureValue other);

        public FeatureValue Clone()
        {
            return CloneImpl(null);
        }
    }
}
