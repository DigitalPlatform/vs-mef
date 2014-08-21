﻿namespace Microsoft.VisualStudio.Composition
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using Validation;

    /// <summary>
    /// Static factory methods for creating .NET Func{T} instances with fewer allocations in some scenarios.
    /// </summary>
    /// <remarks>
    /// These methods employ a neat trick where we take advantage of the fact that Delegate has a field to store
    /// the instance on which to invoke the method. In general, that field is really just the first
    /// argument to pass to the method. So if the method is static, we can use that field to store
    /// something else as the first parameter.
    /// So provided the valueFactory that the caller gave us is a reusable delegate to a static method
    /// that takes one parameter that is a reference type, it means many Func{T} instances can be
    /// constructed for different parameterized values while only incurring the cost of the Func{T} delegate itself
    /// and no closure.
    /// In most cases this is an insignificant difference. But if you're counting allocations for GC pressure,
    /// this might be just what you need. 
    /// </remarks>
    internal static class DelegateServices
    {
        private static readonly Dictionary<Type, MethodInfo> closedReturnTValues = new Dictionary<Type, MethodInfo>();
        private static readonly ConstructorInfo FuncOfObjectCtor = typeof(Func<object>).GetConstructors().Single();
        private static readonly MethodInfo returnObjectValue = typeof(DelegateServices).GetMethod("ReturnObjectValue", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo returnTValue = typeof(DelegateServices).GetMethod("ReturnTValue", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>
        /// Creates a Func{T} from a delegate that takes one parameter
        /// (for the cost of a delegate, but without incurring the cost of a closure).
        /// </summary>
        /// <param name="value">The value to return from the lazy.</param>
        /// <returns>The lazy instance.</returns>
        internal static Func<object> FromValue(object value)
        {
            using (var args = ArrayRental<object>.Get(2))
            {
                args.Value[0] = value;
                args.Value[1] = returnObjectValue.MethodHandle.GetFunctionPointer();
                return (Func<object>)FuncOfObjectCtor.Invoke(args.Value);
            }
        }

        /// <summary>
        /// Creates a Func{T} from a delegate that takes one parameter
        /// (for the cost of a delegate, but without incurring the cost of a closure).
        /// </summary>
        /// <param name="value">The value to return from the lazy.</param>
        /// <returns>The lazy instance.</returns>
        internal static Func<T> FromValue<T>(T value)
        {
            MethodInfo returnTValueClosed = GetFromValueGenericFactoryMethod<T>();

            using (var args = ArrayRental<object>.Get(2))
            {
                args.Value[0] = value;
                args.Value[1] = returnTValueClosed.MethodHandle.GetFunctionPointer();
                return (Func<T>)Helper<T>.FuncOfTCtor.Invoke(args.Value);
            }
        }

        /// <summary>
        /// Creates a Func{T} from a delegate that takes one parameter
        /// (for the cost of a delegate, but without incurring the cost of a closure).
        /// </summary>
        /// <typeparam name="TArg">The type of argument to be passed to the function. If a value type, this will be boxed.</typeparam>
        /// <typeparam name="T">The type of value returned by the function.</typeparam>
        /// <param name="function">The functino.</param>
        /// <param name="arg">The argument to be passed to the function.</param>
        /// <returns>The function constructed with one less argument.</returns>
        internal static Func<T> PresupplyArgument<TArg, T>(this Func<TArg, T> function, TArg arg)
        {
            Requires.NotNull(function, "function");
            Requires.Argument(function.Target == null, "function", "Only static methods or delegates without closures are allowed.");

            using (var args = ArrayRental<object>.Get(2))
            {
                args.Value[0] = arg;
                args.Value[1] = function.Method.MethodHandle.GetFunctionPointer();
                return (Func<T>)Helper<T>.FuncOfTCtor.Invoke(args.Value);
            }
        }

        private static MethodInfo GetFromValueGenericFactoryMethod<T>()
        {
            MethodInfo returnTValueClosed;
            lock (closedReturnTValues)
            {
                closedReturnTValues.TryGetValue(typeof(T), out returnTValueClosed);
            }

            if (returnTValueClosed == null)
            {
                using (var typeArray = ArrayRental<Type>.Get(1))
                {
                    typeArray.Value[0] = typeof(T);
                    returnTValueClosed = returnTValue.MakeGenericMethod(typeArray.Value);
                }

                lock (closedReturnTValues)
                {
                    closedReturnTValues[typeof(T)] = returnTValueClosed;
                }
            }
            return returnTValueClosed;
        }

        private static object ReturnObjectValue(object value)
        {
            return value;
        }

        private static T ReturnTValue<T>(T value)
        {
            return value;
        }

        /// <summary>
        /// A class that caches the results of generic type args in an inexpensive way.
        /// </summary>
        /// <typeparam name="T">The generic type argument used in the cached values.</typeparam>
        private static class Helper<T>
        {
            internal static readonly ConstructorInfo FuncOfTCtor = typeof(Func<T>).GetConstructors().Single();
        }
    }
}
