using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;

namespace ViShap.Viper.Cache;

internal static class FrozenFactoryCache
{
    private static readonly ConcurrentDictionary<(Type Key, Type Value), Func<object, object>> DictionaryFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object>> SetFactories = new();

    public static Func<object, object> GetToFrozenDictionary(Type keyType, Type valueType) =>
        DictionaryFactories.GetOrAdd((keyType, valueType), static key =>
        {
            var sourceType = typeof(IEnumerable<>).MakeGenericType(
                typeof(KeyValuePair<,>).MakeGenericType(key.Key, key.Value));

            var methods = typeof(FrozenDictionary)
                              .GetMethods(BindingFlags.Public | BindingFlags.Static)
                              .Where(static m =>
                                  m.Name == nameof(FrozenDictionary.ToFrozenDictionary))
                              .Where(static m =>
                                  m.IsGenericMethodDefinition &&
                                  m.GetGenericArguments().Length == 2)
                              .Where(static m =>
                              {
                                  var parameters = m.GetParameters();

                                  if (parameters.Length != 2)
                                      return false;

                                  if (!parameters[1].IsOptional)
                                      return false;

                                  var sourceParameter = parameters[0].ParameterType;

                                  if (!sourceParameter.IsGenericType ||
                                      sourceParameter.GetGenericTypeDefinition() != typeof(IEnumerable<>))
                                      return false;

                                  var elementParameter =
                                      sourceParameter.GetGenericArguments()[0];

                                  if (!elementParameter.IsGenericType ||
                                      elementParameter.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
                                      return false;

                                  var keyArguments = elementParameter.GetGenericArguments();
                                  var methodArguments = m.GetGenericArguments();

                                  return keyArguments[0] == methodArguments[0] &&
                                         keyArguments[1] == methodArguments[1];
                              })
                              .SingleOrDefault()
                          ?? throw new BinaryTypeException(
                              $"Could not resolve FrozenDictionary.ToFrozenDictionary<{key.Key.Name}, {key.Value.Name}>().");

            var closedMethod = methods.MakeGenericMethod(key.Key, key.Value);
            var param = Expression.Parameter(typeof(object), "source");
            var source = Expression.Convert(param, sourceType);
            Expression call = closedMethod.GetParameters().Length == 1
                ? Expression.Call(closedMethod, source)
                : Expression.Call(closedMethod, source, Expression.Default(closedMethod.GetParameters()[1].ParameterType));

            return Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), param).Compile();
        });

    public static Func<object, object> GetToFrozenSet(Type elementType) =>
        SetFactories.GetOrAdd(elementType, static t =>
        {
            var sourceType = typeof(IEnumerable<>).MakeGenericType(t);

            var method = typeof(FrozenSet)
                             .GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .Where(static m =>
                                 m.Name == nameof(FrozenSet.ToFrozenSet))
                             .Where(static m =>
                                 m.IsGenericMethodDefinition &&
                                 m.GetGenericArguments().Length == 1)
                             .SingleOrDefault(m =>
                             {
                                 var parameters = m.GetParameters();

                                 if (parameters.Length != 2)
                                     return false;

                                 if (!parameters[1].IsOptional)
                                     return false;

                                 var sourceParameter = parameters[0].ParameterType;

                                 if (!sourceParameter.IsGenericType ||
                                     sourceParameter.GetGenericTypeDefinition() != typeof(IEnumerable<>))
                                     return false;

                                 return sourceParameter.GetGenericArguments()[0] ==
                                        m.GetGenericArguments()[0];
                             })
                         ?? throw new BinaryTypeException(
                             $"Could not resolve FrozenSet.ToFrozenSet<{t.Name}>().");

            var closedMethod = method.MakeGenericMethod(t);

            var param = Expression.Parameter(typeof(object), "source");
            var source = Expression.Convert(param, sourceType);

            Expression call =
                Expression.Call(
                    closedMethod,
                    source,
                    Expression.Default(
                        closedMethod.GetParameters()[1].ParameterType));

            return Expression.Lambda<Func<object, object>>(
                Expression.Convert(call, typeof(object)),
                param).Compile();
        });
}