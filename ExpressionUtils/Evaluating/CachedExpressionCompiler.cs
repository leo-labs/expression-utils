using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace MiaPlaza.ExpressionUtils.Evaluating {
	/// <summary>
	/// A cache for expression compilation. For compilations for structural identical <see cref="ExpressionComparing"/> expressions
	/// this speeds up the compilations significantly (but slows the executions slightly) and avoids the memory leak created when
	/// calling <see cref="LambdaExpression.Compile"/> repeatedly.
	/// </summary>
	/// <remarks>
	/// The result from <see cref="LambdaExpression"/> compilation usually cannot be cached when it contains captured variables 
	/// or constants (closures) that should be replaced in later calls. This cache first extracts all constants from the expression
	/// and will then look up the normalized (constant free) expression in a compile cache. The constants then get re-inserted into 
	/// the result via a closure and a delegate capturing the actual parameters of the original expression is returned.
	/// </remarks>
	public class CachedExpressionCompiler : IExpressionEvaluator {
		static ConcurrentDictionary<LambdaExpression, ParameterListDelegate> delegates = new ConcurrentDictionary<LambdaExpression, ParameterListDelegate>(new ExpressionComparing.StructuralComparer(ignoreConstantsValues: true));
		public static readonly CachedExpressionCompiler Instance = new CachedExpressionCompiler();

		private CachedExpressionCompiler() { }

		VariadicArrayParametersDelegate IExpressionEvaluator.EvaluateLambda(LambdaExpression lambdaExpression) => CachedCompileLambda(lambdaExpression);
		public VariadicArrayParametersDelegate CachedCompileLambda(LambdaExpression lambda) {
			IReadOnlyList<object> constants;

			ParameterListDelegate compiled;
			if (delegates.TryGetValue(lambda, out compiled)) {
				constants = ConstantExtractor.ExtractConstantsOnly(lambda.Body);
			} else {
				var extractionResult = ConstantExtractor.ExtractConstants(lambda.Body);

				compiled = ParameterListRewriter.RewriteLambda(
					Expression.Lambda(
						extractionResult.ConstantfreeExpression.Body,
						extractionResult.ConstantfreeExpression.Parameters.Concat(lambda.Parameters)))
					.Compile();
				delegates.TryAdd(lambda, compiled);
				constants = extractionResult.ExtractedConstants;
			}

			return args => compiled(constants.Concat(args).ToArray());
		}

		object IExpressionEvaluator.Evaluate(Expression unparametrizedExpression) => CachedCompile(unparametrizedExpression);
		public object CachedCompile(Expression unparametrizedExpression) => CachedCompileLambda(Expression.Lambda(unparametrizedExpression))();

		DELEGATE IExpressionEvaluator.EvaluateTypedLambda<DELEGATE>(Expression<DELEGATE> expression) => CachedCompileTypedLambda(expression);
		public DELEGATE CachedCompileTypedLambda<DELEGATE>(Expression<DELEGATE> expression) where DELEGATE : class => CachedCompileLambda(expression).WrapDelegate<DELEGATE>();
	}
}
