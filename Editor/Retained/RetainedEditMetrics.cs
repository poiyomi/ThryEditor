#if UNITY_2021_3_OR_NEWER
using System;
using System.Diagnostics;

namespace Thry.ThryEditor
{
    // Opt-in diagnostics for the complete retained edit transaction. In ordinary
    // use the scopes do not read the clock or allocate. Nested edit callbacks can
    // overlap phase totals; profile a representative direct edit to attribute cost.
    internal static class RetainedEditMetrics
    {
        internal enum Phase { Total, Prepare, Mutation, ApplyDrawers, PropertyCallbacks, LinkedMaterials, EditorInvalidation, FinalRefresh, Synchronize, NativeRefresh, AnimationMetadata, TextureKeywords, AnimationIndicators, NativeValues, AnimationPreparation, PropertyReferences }
        internal static bool Enabled;
        static readonly long[] Ticks = new long[Enum.GetValues(typeof(Phase)).Length];
        static readonly int[] Samples = new int[Ticks.Length];
        internal static string[] Names => Enum.GetNames(typeof(Phase));
        internal static int[] Counts => (int[])Samples.Clone();
        internal static double[] Milliseconds
        {
            get { var values = new double[Ticks.Length]; for(int i=0;i<values.Length;i++) values[i]=Ticks[i]*1000d/Stopwatch.Frequency;return values; }
        }
        internal static void Reset() { Array.Clear(Ticks,0,Ticks.Length);Array.Clear(Samples,0,Samples.Length); }
        internal static Scope Measure(Phase phase) => new Scope(phase);
        internal readonly struct Scope : IDisposable
        {
            readonly Phase _phase;
            readonly bool _enabled;
            readonly long _start;
            internal Scope(Phase phase) { _phase=phase;_enabled=Enabled;_start=_enabled?Stopwatch.GetTimestamp():0; }
            public void Dispose() { if(!_enabled)return;int index=(int)_phase;Ticks[index]+=Stopwatch.GetTimestamp()-_start;Samples[index]++; }
        }
    }
}
#endif
