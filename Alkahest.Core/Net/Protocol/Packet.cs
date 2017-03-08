using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Alkahest.Core.Net.Protocol
{
    public abstract class Packet
    {
        const BindingFlags FieldFlags =
            BindingFlags.DeclaredOnly |
            BindingFlags.Instance |
            BindingFlags.Public;

        public abstract string OpCode { get; }

        internal virtual void OnSerialize()
        {
        }

        internal virtual void OnDeserialize()
        {
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            ToString(sb, this, OpCode, string.Empty);

            return sb.ToString();
        }

        static void ToString(StringBuilder builder, object source,
            string header, string indent)
        {
            var type = source.GetType();

            builder.AppendLine($"{indent}{header}");
            builder.AppendLine($"{indent}{{");

            foreach (var prop in type.GetProperties(FieldFlags)
                .Where(p => p.GetCustomAttribute<PacketFieldAttribute>() != null)
                .OrderBy(p => p.MetadataToken))
            {
                var propType = prop.PropertyType;
                var name = prop.Name;
                var value = prop.GetValue(source);

                if (propType.IsArray)
                {
                    var array = (Array)value;

                    builder.AppendLine($"{indent}  {name} = [{array.Length}]");
                    builder.AppendLine($"{indent}  {{");

                    var elemIndent = indent + "    ";
                    var elemType = propType.GetElementType();

                    for (var i = 0; i < array.Length; i++)
                    {
                        var elem = array.GetValue(i);
                        var idx = $"[{i}] =";

                        if (elemType.IsPrimitive)
                            builder.AppendLine($"{elemIndent}{idx} {elem}");
                        else if (elemType == typeof(string))
                            builder.AppendLine($"{elemIndent}{idx} \"{elem}\"");
                        else
                            ToString(builder, elem, idx, elemIndent);
                    }

                    builder.AppendLine($"{indent}  }}");
                }
                else if (propType.IsPrimitive)
                    builder.AppendLine($"{indent}  {name} = {value}");
                else
                    builder.AppendLine($"{indent}  {name} = \"{value}\"");
            }

            builder.AppendLine($"{indent}}}");
        }
    }
}
