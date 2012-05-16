﻿using System;
using System.Linq;
using Xunit;
using FluentCassandra.Types;

namespace FluentCassandra
{
	
	public class FluentSuperColumnTest
	{
		[Fact]
		public void Parent_Set()
		{
			// arrange

			// act
			var actual = new FluentSuperColumn<AsciiType, AsciiType>();

			// assert
			Assert.Same(actual, actual.GetPath().SuperColumn);
		}

		[Fact]
		public void Path_Set()
		{
			// arrange

			// act
			var actual = new FluentSuperColumn<AsciiType, AsciiType>();

			// assert
			Assert.Same(actual, actual.GetPath().SuperColumn);
		}

		[Fact]
		public void Constructor_Test()
		{
			// arrange
			var colName = "This is a test name";
			var col1 = new FluentColumn<AsciiType> { ColumnName = "Test1", ColumnValue = 300M };
			var col2 = new FluentColumn<AsciiType> { ColumnName = "Test2", ColumnValue = "Hello" };

			// act
			var actual = new FluentSuperColumn<AsciiType, AsciiType>();
			actual.ColumnName = colName;
			actual.Columns.Add(col1);
			actual.Columns.Add(col2);

			// assert
			Assert.Equal(colName, (string)actual.ColumnName);
			Assert.Equal(2, actual.Columns.Count);
		}

		[Fact]
		public void Constructor_Dynamic_Test()
		{
			// arrange
			var colName = "This is a test name";
			var col1 = "Test1";
			var colValue1 = 300M;
			var col2 = "Test2";
			var colValue2 = "Hello";

			// act
			dynamic actual = new FluentSuperColumn<AsciiType, AsciiType>();
			actual.Name = colName;
			actual.Test1 = colValue1;
			actual.Test2 = colValue2;

			// assert
			Assert.Equal(colName, (string)actual.Name);
			//Assert.Equal(2, actual.Columns.CountColumns);
			Assert.Equal(colValue1, (decimal)actual.Test1);
			Assert.Equal(colValue1, (decimal)actual[col1]);
			Assert.Equal(colValue2, (string)actual.Test2);
			Assert.Equal(colValue2, (string)actual[col2]);
		}

		[Fact]
		public void Get_NonExistent_Column()
		{
			// arrange
			var colName = "This is a test name";
			var colValue1 = 300M;
			var colValue2 = "Hello";
			var expected = (string)null;

			// act
			dynamic superColumn = new FluentSuperColumn<AsciiType, AsciiType>();
			superColumn.Name = colName;
			superColumn.Test1 = colValue1;
			superColumn.Test2 = colValue2;
			var actual = superColumn.Test3;

			// assert
			Assert.Equal(expected, (string)actual);
		}

		[Fact]
		public void Mutation()
		{
			// arrange
			var colName = "This is a test name";
			var col1 = new FluentColumn<AsciiType> { ColumnName = "Test1", ColumnValue = 300M };
			var col2 = new FluentColumn<AsciiType> { ColumnName = "Test2", ColumnValue = "Hello" };

			// act
			var actual = new FluentSuperColumn<AsciiType, AsciiType>();
			actual.ColumnName = colName;
			actual.Columns.Add(col1);
			actual.Columns.Add(col2);

			// assert
			var mutations = actual.MutationTracker.GetMutations();

			Assert.Equal(2, mutations.Count());
			Assert.Equal(2, mutations.Count(x => x.Type == MutationType.Added));

			var mut1 = mutations.FirstOrDefault(x => x.Column.ColumnName == "Test1");
			var mut2 = mutations.FirstOrDefault(x => x.Column.ColumnName == "Test2");

			Assert.Same(col1, mut1.Column);
			Assert.Same(col2, mut2.Column);

			Assert.Same(actual, mut1.Column.GetParent().SuperColumn);
			Assert.Same(actual, mut2.Column.GetParent().SuperColumn);
		}

		[Fact]
		public void Dynamic_Mutation()
		{
			// arrange
			var colName = "This is a test name";
			var col1 = "Test1";
			var colValue1 = 300M;
			var col2 = "Test2";
			var colValue2 = "Hello";

			// act
			dynamic actual = new FluentSuperColumn<AsciiType, AsciiType>();
			actual.ColumnName = colName;
			actual.Test1 = colValue1;
			actual.Test2 = colValue2;

			// assert
			var mutations = ((IFluentRecord)actual).MutationTracker.GetMutations();

			Assert.Equal(2, mutations.Count());
			Assert.Equal(2, mutations.Count(x => x.Type == MutationType.Added));

			var mut1 = mutations.FirstOrDefault(x => x.Column.ColumnName == col1);
			var mut2 = mutations.FirstOrDefault(x => x.Column.ColumnName == col2);

			Assert.NotNull(mut1);
			Assert.NotNull(mut2);

			Assert.Same(actual, mut1.Column.GetParent().SuperColumn);
			Assert.Same(actual, mut2.Column.GetParent().SuperColumn);
		}
	}
}
