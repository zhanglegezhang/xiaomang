using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

namespace ShareX.HelpersLib
{
    // Copies the persisted object graph on its owner thread. JSON formatting, encryption
    // and disk access can then run without enumerating live UI-owned collections.
    public sealed class SettingsObjectSnapshot<T> : IDisposable where T : class
    {
        private static readonly WritablePropertiesOnlyResolver resolver = new();
        private static readonly MethodInfo memberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Dictionary<object, object> copies = new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        private readonly List<IDisposable> owned = new();
        public T Value { get; private set; }

        public static SettingsObjectSnapshot<T> Capture(T source)
        {
            ArgumentNullException.ThrowIfNull(source);
            var snapshot = new SettingsObjectSnapshot<T>();
            try { snapshot.Value = (T)snapshot.Copy(source); return snapshot; }
            catch { snapshot.Dispose(); throw; }
        }

        private object Copy(object source)
        {
            if (source == null) return null;
            Type type = source.GetType();
            if (type.IsValueType || source is string || source is Uri || source is TimeZoneInfo || source is Version) return source;
            if (copies.TryGetValue(source, out object existing)) return existing;
            if (source is Font font)
            {
                var clone = (Font)font.Clone(); owned.Add(clone); copies.Add(source, clone); return clone;
            }
            if (source is JToken token) { var clone = token.DeepClone(); copies.Add(source, clone); return clone; }
            if (source is Array array)
            {
                if (array.Rank != 1) throw new NotSupportedException("Multidimensional settings arrays are not supported.");
                var clone = (Array)array.Clone(); copies.Add(source, clone);
                for (int i = array.GetLowerBound(0); i <= array.GetUpperBound(0); i++) clone.SetValue(Copy(array.GetValue(i)), i);
                return clone;
            }
            if (source is IDictionary dictionary)
            {
                var clone = (IDictionary)Activator.CreateInstance(type); copies.Add(source, clone);
                foreach (DictionaryEntry item in dictionary) clone.Add(Copy(item.Key), Copy(item.Value));
                return clone;
            }
            if (source is IList list)
            {
                var clone = (IList)Activator.CreateInstance(type); copies.Add(source, clone);
                foreach (object item in list) clone.Add(Copy(item));
                return clone;
            }
            JsonContract contract = resolver.ResolveContract(type);
            // String/primitive contracts represent immutable scalar settings (e.g. Version).
            if (contract is not JsonObjectContract objectContract)
                throw new NotSupportedException("Unsupported settings snapshot type: " + type.FullName);
            object result = memberwiseClone.Invoke(source, null); copies.Add(source, result);
            foreach (JsonProperty property in objectContract.Properties)
            {
                if (property.Ignored || !property.Readable || !property.Writable) continue;
                object original = property.ValueProvider.GetValue(source);
                object copied = Copy(original);
                if (!ReferenceEquals(original, copied)) property.ValueProvider.SetValue(result, copied);
            }
            return result;
        }

        public void Dispose()
        {
            foreach (IDisposable resource in owned) resource.Dispose();
            owned.Clear(); copies.Clear(); Value = null;
        }
    }
}
