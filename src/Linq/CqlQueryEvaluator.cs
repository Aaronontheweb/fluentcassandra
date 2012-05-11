using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FluentCassandra.Types;
using FluentCassandra.ObjectSerializer;

namespace FluentCassandra.Linq
{
	/// <see href="https://github.com/apache/cassandra/blob/trunk/doc/cql/CQL.textile"/>
	/// <see href="http://www.datastax.com/docs/1.0/references/cql/index"/>
	internal class CqlQueryEvaluator
	{
		private string _columnFamily;
		private readonly ObjectSerializerConventions _conventions;

		internal CqlQueryEvaluator(ObjectSerializerConventions conventions = null)
		{
			FieldsArray = new List<string>();

			_conventions = conventions;
		}

		public string Query
		{
			get
			{
				var select = Fields;
				var from = _columnFamily;
				var where = WhereCriteria;
				var limit = LimitCount;

				var query = String.Format("SELECT {0} \nFROM {1}", select, from);

				if (!String.IsNullOrWhiteSpace(where))
					query += " \nWHERE " + where;

				if (limit > 0)
					query += " \nLIMIT " + limit;

				return query;
			}
		}

		private IList<string> FieldsArray { get; set; }

		private int FirstCount { get; set; }

		private int LimitCount { get; set; }

		private string Fields
		{
			get
			{
				var firstCount = FirstCount;
				var query = "";

				if (firstCount > 0)
					query += "FIRST " + firstCount;

				if (query.Length > 0)
					query += " ";

				if (FieldsArray.Count == 0)
				{
					query += "*";
					return query;
				}

				return String.Join(", ", FieldsArray.ToArray());
			}
		}

		private string WhereCriteria { get; set; }

		private void AddTable(ICassandraColumnFamilyInfo provider)
		{
			_columnFamily = provider.FamilyName;
		}

		private void AddField(Expression exp)
		{
			foreach (var f in VisitSelectExpression(exp))
				FieldsArray.Add(f);
		}

		private void AddCriteria(Expression exp)
		{
			string newCriteria = VisitWhereExpression(exp);

			if (!String.IsNullOrEmpty(WhereCriteria))
				WhereCriteria = "(" + WhereCriteria + " AND " + newCriteria + ")";
			else
				WhereCriteria = newCriteria;
		}

		#region Expression Helpers

		private Expression SimplifyExpression(Expression exp)
		{
			switch (exp.NodeType)
			{
				case ExpressionType.Convert:
				case ExpressionType.Quote:
					return SimplifyExpression(((UnaryExpression)exp).Operand);

				case ExpressionType.Lambda:
					return SimplifyExpression(((LambdaExpression)exp).Body);

				default:
					return exp;
			}
		}

		private Type GetBaseType(MemberExpression exp)
		{
			while (exp.Expression.NodeType == ExpressionType.MemberAccess)
				exp = (MemberExpression)exp.Expression;

			return exp.Expression.Type;
		}

		private string GetPropertyName(Expression exp)
		{
			exp = SimplifyExpression(exp);

			if (exp.NodeType != ExpressionType.Call && exp.NodeType != ExpressionType.MemberAccess)
				throw new NotSupportedException(exp.NodeType.ToString() + " is not supported.");

			if (exp.NodeType == ExpressionType.MemberAccess)
			{
				var memExp = (MemberExpression)exp;
				var name = memExp.Member.Name;

				// if object queries
				if (GetBaseType(memExp) != typeof(ICqlRow))
				{
					if (_conventions.KeyPropertyNames.Contains(name, _conventions.AreKeyPropertyNamesCaseSensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
						name = "KEY";

					return name;
				}

				if (name != "Key")
					throw new NotSupportedException(name + " is not a supported property.");

				return "KEY";
			}

			var field = SimplifyExpression(((MethodCallExpression)exp).Arguments[0]);

			if (field.NodeType != ExpressionType.Constant)
				throw new NotSupportedException(exp.NodeType.ToString() + " is not supported.");

			return ((ConstantExpression)field).Value.ToString();
		}

		#endregion

		#region Expression Parsing

		public static string GetCql(Expression expression, ObjectSerializerConventions conventions = null)
		{
			var eval = new CqlQueryEvaluator(conventions);
			eval.Evaluate(expression);

			return eval.Query;
		}

		private void Evaluate(Expression exp, Action<string> call = null)
		{
			switch (exp.NodeType)
			{
				case ExpressionType.Lambda:
					Evaluate(((LambdaExpression)exp).Body);
					break;

				case ExpressionType.Call:
					VisitMethodCall((MethodCallExpression)exp);
					break;

				case ExpressionType.MemberInit:
					VisitMemberInit((MemberInitExpression)exp, call);
					break;

				case ExpressionType.Constant:
					var obj = ((ConstantExpression)exp).Value;
					var family = obj as ICassandraColumnFamilyInfo;

					AddTable(family);
					break;
			}
		}

		private void VisitMemberInit(MemberInitExpression exp, Action<string> call)
		{
			foreach (MemberAssignment member in exp.Bindings)
				call(GetPropertyName(member.Expression));
		}

		private void VisitMethodCall(MethodCallExpression exp)
		{
			Evaluate(exp.Arguments[0]);

			if (exp.Method.Name == "Where")
				AddCriteria(exp.Arguments[1]);
			else if (exp.Method.Name == "Select")
				AddField(SimplifyExpression(exp.Arguments[1]));
			else if (exp.Method.Name == "Take")
				SetLimit(exp.Arguments[1]);
			else
				throw new NotSupportedException("Method call to " + exp.Method.Name + " is not supported.");
		}

		private void SetLimit(Expression exp)
		{
			if (exp.NodeType == ExpressionType.Constant)
				LimitCount = (int)((ConstantExpression)exp).Value;
		}

		private IEnumerable<CassandraObject> VisitSelectExpression(Expression exp)
		{
			switch (exp.NodeType)
			{
				case ExpressionType.Parameter:
					return new CassandraObject[0];

				case ExpressionType.Constant:
					return VisitSelectColumnExpression((ConstantExpression)exp);

				case ExpressionType.MemberAccess:
					var list = new List<CassandraObject>();
					VisitSelectMemberAccess((MemberExpression)exp, x => {
						list.Add(x);
					});
					return list;

				case ExpressionType.New:
					return VisitSelectNew((NewExpression)exp);

				default:
					throw new NotSupportedException(exp.NodeType.ToString() + " is not supported.");
			}
		}

		private IEnumerable<CassandraObject> VisitSelectNew(NewExpression exp)
		{
			var list = new List<CassandraObject>();

			foreach (var arg in exp.Arguments)
				VisitSelectMemberAccess((MemberExpression)arg, x => {
					list.Add(x);
				});

			return list;
		}

		private void VisitSelectMemberAccess(MemberExpression exp, Action<string> call)
		{
			var name = exp.Member.Name;
			call(name);
		}

		private IEnumerable<CassandraObject> VisitSelectColumnExpression(ConstantExpression exp)
		{
			return (IEnumerable<CassandraObject>)exp.Value;
		}

		private string VisitWhereExpression(Expression exp)
		{
			switch (exp.NodeType)
			{
				case ExpressionType.Convert:
				case ExpressionType.Lambda:
				case ExpressionType.Quote:
					return VisitWhereExpression(SimplifyExpression(exp));

				case ExpressionType.Not:
					return VisitWhereUnaryExpression((UnaryExpression)exp);

				case ExpressionType.Equal:
				case ExpressionType.NotEqual:
				case ExpressionType.GreaterThan:
				case ExpressionType.GreaterThanOrEqual:
				case ExpressionType.LessThan:
				case ExpressionType.LessThanOrEqual:
					return VisitWhereRelationalExpression((BinaryExpression)exp);

				case ExpressionType.And:
				case ExpressionType.AndAlso:
				case ExpressionType.Or:
				case ExpressionType.OrElse:
					return VisitWhereConditionalExpression((BinaryExpression)exp);

				case ExpressionType.Call:
					return VisitWhereMethodCallExpression((MethodCallExpression)exp);

				default:
					throw new NotSupportedException(exp.NodeType.ToString() + " is not supported.");
			}
		}

		private string VisitWhereUnaryExpression(UnaryExpression exp)
		{
			switch (exp.NodeType)
			{
				case ExpressionType.Not:
					return "NOT (" + VisitWhereExpression(exp.Operand) + ")";

				default:
					throw new NotSupportedException(exp.NodeType.ToString() + " is not a supported unary criteria.");
			}
		}

		private string RightObjectToString(object obj)
		{
			string value = obj.ToString();
			if (obj is String)
				return String.Concat("'", value, "'");
			return value;
		}

		private string VisitWhereMethodCallExpression(MethodCallExpression exp)
		{
			if (exp.Method.Name == "Contains")
			{
				var left = GetPropertyName(exp.Arguments[1]);
				var values = (IEnumerable)Expression.Lambda(exp.Arguments[0]).Compile().DynamicInvoke();
				var rightArray = new List<string>();
				foreach (var obj in values)
					rightArray.Add(RightObjectToString(obj));
				var right = String.Join(",", rightArray);

				return left + " IN (" + right + ")";
			}
			else
				throw new NotSupportedException("Method call to " + exp.Method.Name + " is not supported.");
		}

		private string VisitWhereRelationalExpression(BinaryExpression exp)
		{
			string criteria;

			string left = GetPropertyName(exp.Left);
			object rightObj = Expression.Lambda(exp.Right).Compile().DynamicInvoke();
			string right = RightObjectToString(rightObj);

			if (left == "KEY")
			{
				switch (exp.NodeType)
				{
					case ExpressionType.Equal:
						criteria = left + " = " + right;
						break;
					case ExpressionType.GreaterThan:
						criteria = left + " > " + right;
						break;
					case ExpressionType.GreaterThanOrEqual:
						criteria = left + " >= " + right;
						break;
					case ExpressionType.LessThan:
						criteria = left + " < " + right;
						break;
					case ExpressionType.LessThanOrEqual:
						criteria = left + " <= " + right;
						break;

					default:
						throw new NotSupportedException(
							exp.NodeType.ToString() + " is not a supported relational criteria for KEY.");
				}
			}
			else
			{
				switch (exp.NodeType)
				{
					case ExpressionType.Equal:
						criteria = left + " = " + right;
						break;

					default:
						throw new NotSupportedException(
							exp.NodeType.ToString() + " is not a supported relational criteria for Columns.");
				}
			}

			return criteria;
		}

		private string VisitWhereConditionalExpression(BinaryExpression exp)
		{
			string criteria;

			switch (exp.NodeType)
			{
				case ExpressionType.And:
				case ExpressionType.AndAlso:
					criteria = VisitWhereExpression(exp.Left) + " AND " + VisitWhereExpression(exp.Right);
					break;

				default:
					throw new NotSupportedException(exp.NodeType.ToString() + " is not a supported conditional criteria.");
			}

			return criteria;
		}
		
		#endregion
	}
}