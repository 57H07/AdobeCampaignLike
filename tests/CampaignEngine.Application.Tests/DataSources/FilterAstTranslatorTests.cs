using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Filters;
using CampaignEngine.Infrastructure.DataSources;

namespace CampaignEngine.Application.Tests.DataSources;

/// <summary>
/// Unit tests for <see cref="FilterAstTranslator"/>.
/// Covers:
///   - TASK-016-07: AST to SQL translation tests
///   - TASK-016-08: SQL injection prevention in filter values
///   - TASK-016-09: Complex filter logic tests (AND/OR)
/// </summary>
public class FilterAstTranslatorTests
{
    private readonly FilterAstTranslator _sut = new();

    // -----------------------------------------------------------------------
    // Helper: build a simple field definition
    // -----------------------------------------------------------------------
    private static FieldDefinitionDto Field(string name, string type = "nvarchar", bool filterable = true)
        => new() { FieldName = name, DisplayName = name, FieldType = type, IsFilterable = filterable };

    private static IReadOnlyList<FieldDefinitionDto> Fields(params FieldDefinitionDto[] fields)
        => fields.ToList();

    // -----------------------------------------------------------------------
    // TASK-016-07: Basic operator translations
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Equals operator translates to '=' with parameter")]
    public void Translate_Equals_ProducesEqualsClause()
    {
        var filter = FilterExpression.Leaf("Email", FilterOperator.Equals, "test@example.com");
        var result = _sut.TranslateSingle(filter, Fields(Field("Email")));

        result.WhereClause.Should().Be("[Email] = @p0");
        result.Parameters.Should().ContainKey("@p0")
            .WhoseValue.Should().Be("test@example.com");
    }

    [Fact(DisplayName = "NotEquals operator translates to '<>'")]
    public void Translate_NotEquals_ProducesNotEqualsClause()
    {
        var filter = FilterExpression.Leaf("Status", FilterOperator.NotEquals, "inactive");
        var result = _sut.TranslateSingle(filter, Fields(Field("Status")));

        result.WhereClause.Should().Be("[Status] <> @p0");
    }

    [Fact(DisplayName = "GreaterThan operator translates to '>'")]
    public void Translate_GreaterThan_ProducesGtClause()
    {
        var filter = FilterExpression.Leaf("Age", FilterOperator.GreaterThan, 18);
        var result = _sut.TranslateSingle(filter, Fields(Field("Age", "int")));

        result.WhereClause.Should().Be("[Age] > @p0");
        result.Parameters["@p0"].Should().Be(18);
    }

    [Fact(DisplayName = "LessThan operator translates to '<'")]
    public void Translate_LessThan_ProducesLtClause()
    {
        var filter = FilterExpression.Leaf("Score", FilterOperator.LessThan, 100);
        var result = _sut.TranslateSingle(filter, Fields(Field("Score", "int")));

        result.WhereClause.Should().Be("[Score] < @p0");
    }

    [Fact(DisplayName = "GreaterThanOrEquals translates to '>='")]
    public void Translate_Gte_ProducesGteClause()
    {
        var filter = FilterExpression.Leaf("Balance", FilterOperator.GreaterThanOrEquals, 0m);
        var result = _sut.TranslateSingle(filter, Fields(Field("Balance", "decimal")));

        result.WhereClause.Should().Be("[Balance] >= @p0");
    }

    [Fact(DisplayName = "LessThanOrEquals translates to '<='")]
    public void Translate_Lte_ProducesLteClause()
    {
        var filter = FilterExpression.Leaf("Priority", FilterOperator.LessThanOrEquals, 5);
        var result = _sut.TranslateSingle(filter, Fields(Field("Priority", "int")));

        result.WhereClause.Should().Be("[Priority] <= @p0");
    }

    [Fact(DisplayName = "LIKE operator translates to LIKE with parameter")]
    public void Translate_Like_ProducesLikeClause()
    {
        var filter = FilterExpression.Leaf("Name", FilterOperator.Like, "%John%");
        var result = _sut.TranslateSingle(filter, Fields(Field("Name")));

        result.WhereClause.Should().Be("[Name] LIKE @p0");
        result.Parameters["@p0"].Should().Be("%John%");
    }

    [Fact(DisplayName = "IsNull operator produces IS NULL with no parameter")]
    public void Translate_IsNull_ProducesIsNullClause()
    {
        var filter = FilterExpression.Leaf("DeletedAt", FilterOperator.IsNull);
        var result = _sut.TranslateSingle(filter, Fields(Field("DeletedAt", "datetime")));

        result.WhereClause.Should().Be("[DeletedAt] IS NULL");
        result.Parameters.Should().BeEmpty();
    }

    [Fact(DisplayName = "IsNotNull operator produces IS NOT NULL with no parameter")]
    public void Translate_IsNotNull_ProducesIsNotNullClause()
    {
        var filter = FilterExpression.Leaf("Email", FilterOperator.IsNotNull);
        var result = _sut.TranslateSingle(filter, Fields(Field("Email")));

        result.WhereClause.Should().Be("[Email] IS NOT NULL");
        result.Parameters.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // TASK-016-07: IN operator translation
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "IN operator with list of values expands to individual parameters")]
    public void Translate_In_ExpandsToMultipleParameters()
    {
        var filter = FilterExpression.Leaf("Status", FilterOperator.In, new[] { "active", "trial", "pending" });
        var result = _sut.TranslateSingle(filter, Fields(Field("Status")));

        result.WhereClause.Should().Be("[Status] IN (@p0_0, @p0_1, @p0_2)");
        result.Parameters.Should().HaveCount(3);
        result.Parameters["@p0_0"].Should().Be("active");
        result.Parameters["@p0_1"].Should().Be("trial");
        result.Parameters["@p0_2"].Should().Be("pending");
    }

    [Fact(DisplayName = "IN operator with empty list produces '1=0' sentinel")]
    public void Translate_InEmpty_Produces1Equals0()
    {
        var filter = FilterExpression.Leaf("Status", FilterOperator.In, new string[0]);
        var result = _sut.TranslateSingle(filter, Fields(Field("Status")));

        result.WhereClause.Should().Be("1=0");
        result.Parameters.Should().BeEmpty();
    }

    [Fact(DisplayName = "IN operator exceeding 1000 values throws")]
    public void Translate_InExceeds1000_Throws()
    {
        var values = Enumerable.Range(1, 1001).Select(i => (object)i).ToArray();
        var filter = FilterExpression.Leaf("Id", FilterOperator.In, values);

        var act = () => _sut.TranslateSingle(filter, Fields(Field("Id", "int")));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*1000*");
    }

    // -----------------------------------------------------------------------
    // TASK-016-07: Multiple top-level filters (implicit AND)
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Multiple top-level filters are joined with AND")]
    public void Translate_MultipleTopLevel_JoinedWithAnd()
    {
        var filters = new[]
        {
            FilterExpression.Leaf("Age", FilterOperator.GreaterThan, 18),
            FilterExpression.Leaf("Status", FilterOperator.Equals, "active")
        };

        var fields = Fields(Field("Age", "int"), Field("Status"));
        var result = _sut.Translate(filters, fields);

        result.WhereClause.Should().Be("[Age] > @p0 AND [Status] = @p1");
        result.Parameters.Should().HaveCount(2);
    }

    [Fact(DisplayName = "Empty filter list returns null WHERE clause")]
    public void Translate_Empty_ReturnsNullWhereClause()
    {
        var result = _sut.Translate([], Fields(Field("Name")));

        result.WhereClause.Should().BeNull();
        result.Parameters.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // TASK-016-09: Complex AND/OR logic
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "CompositeAND node wraps children in parentheses with AND")]
    public void Translate_CompositeAnd_WrapsInParentheses()
    {
        var composite = FilterExpression.And(
            FilterExpression.Leaf("Age", FilterOperator.GreaterThan, 18),
            FilterExpression.Leaf("IsActive", FilterOperator.Equals, true)
        );

        var fields = Fields(Field("Age", "int"), Field("IsActive", "bit"));
        var result = _sut.TranslateSingle(composite, fields);

        result.WhereClause.Should().Be("([Age] > @p0 AND [IsActive] = @p1)");
    }

    [Fact(DisplayName = "CompositeOR node wraps children in parentheses with OR")]
    public void Translate_CompositeOr_WrapsInParentheses()
    {
        var composite = FilterExpression.Or(
            FilterExpression.Leaf("Country", FilterOperator.Equals, "FR"),
            FilterExpression.Leaf("Country", FilterOperator.Equals, "DE")
        );

        var fields = Fields(Field("Country"));
        var result = _sut.TranslateSingle(composite, fields);

        result.WhereClause.Should().Be("([Country] = @p0 OR [Country] = @p1)");
    }

    [Fact(DisplayName = "Nested AND with OR produces correct nesting")]
    public void Translate_NestedAndOr_CorrectStructure()
    {
        // WHERE Age > 18 AND (Country = 'FR' OR Country = 'DE')
        var filters = new FilterExpression[]
        {
            FilterExpression.Leaf("Age", FilterOperator.GreaterThan, 18),
            FilterExpression.Or(
                FilterExpression.Leaf("Country", FilterOperator.Equals, "FR"),
                FilterExpression.Leaf("Country", FilterOperator.Equals, "DE")
            )
        };

        var fields = Fields(Field("Age", "int"), Field("Country"));
        var result = _sut.Translate(filters, fields);

        result.WhereClause.Should().Be("[Age] > @p0 AND ([Country] = @p1 OR [Country] = @p2)");
        result.Parameters.Should().HaveCount(3);
    }

    [Fact(DisplayName = "Deeply nested OR inside AND inside AND is supported")]
    public void Translate_DeepNesting_Supported()
    {
        // AND(IsActive=true, AND(Age>18, OR(Role='admin', Role='manager')))
        var filter = FilterExpression.And(
            FilterExpression.Leaf("IsActive", FilterOperator.Equals, true),
            FilterExpression.And(
                FilterExpression.Leaf("Age", FilterOperator.GreaterThan, 18),
                FilterExpression.Or(
                    FilterExpression.Leaf("Role", FilterOperator.Equals, "admin"),
                    FilterExpression.Leaf("Role", FilterOperator.Equals, "manager")
                )
            )
        );

        var fields = Fields(Field("IsActive", "bit"), Field("Age", "int"), Field("Role"));
        var result = _sut.TranslateSingle(filter, fields);

        result.WhereClause.Should().NotBeNullOrEmpty();
        result.WhereClause.Should().Contain("OR");
        result.WhereClause.Should().Contain("AND");
        result.Parameters.Should().HaveCount(4);
    }

    [Fact(DisplayName = "Nesting beyond max depth (5) throws InvalidOperationException")]
    public void Translate_ExceedsMaxDepth_Throws()
    {
        // Build a chain of 6 nested AND composites
        FilterExpression deepExpr = FilterExpression.Leaf("Id", FilterOperator.Equals, 1);
        for (var i = 0; i < 6; i++)
        {
            deepExpr = FilterExpression.And(deepExpr, FilterExpression.Leaf("Id", FilterOperator.Equals, i + 2));
        }

        var act = () => _sut.TranslateSingle(deepExpr, Fields(Field("Id", "int")));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*nesting depth*");
    }

    // -----------------------------------------------------------------------
    // TASK-016-08: SQL injection prevention
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "String value with SQL injection payload is parameterized, not interpolated")]
    public void Translate_SqlInjectionInValue_IsParameterized()
    {
        var injection = "'; DROP TABLE Contacts; --";
        var filter = FilterExpression.Leaf("Email", FilterOperator.Equals, injection);

        var result = _sut.TranslateSingle(filter, Fields(Field("Email")));

        // The WHERE clause must contain a parameter reference, not the raw injection string
        result.WhereClause.Should().Be("[Email] = @p0");
        result.WhereClause.Should().NotContain("DROP TABLE");
        result.WhereClause.Should().NotContain(injection);

        // The value should be in parameters, where the SQL driver will properly escape it
        result.Parameters["@p0"].Should().Be(injection);
    }

    [Fact(DisplayName = "LIKE value with SQL injection payload is parameterized")]
    public void Translate_SqlInjectionInLikeValue_IsParameterized()
    {
        var injection = "% OR 1=1 --";
        var filter = FilterExpression.Leaf("Name", FilterOperator.Like, injection);

        var result = _sut.TranslateSingle(filter, Fields(Field("Name")));

        result.WhereClause.Should().Be("[Name] LIKE @p0");
        result.WhereClause.Should().NotContain("1=1");
        result.Parameters["@p0"].Should().Be(injection);
    }

    [Fact(DisplayName = "IN values with SQL injection are individually parameterized")]
    public void Translate_SqlInjectionInInValues_AreParameterized()
    {
        var values = new[] { "'; DELETE FROM Users; --", "normal_value" };
        var filter = FilterExpression.Leaf("Status", FilterOperator.In, values);

        var result = _sut.TranslateSingle(filter, Fields(Field("Status")));

        // No injection strings should appear in the WHERE clause
        result.WhereClause.Should().NotContain("DELETE");
        result.WhereClause.Should().NotContain("Users");
        result.Parameters.Values.Should().Contain("'; DELETE FROM Users; --");
    }

    [Fact(DisplayName = "Field name not in schema throws to prevent column name injection")]
    public void Translate_FieldNotInSchema_Throws()
    {
        var filter = FilterExpression.Leaf("'; DROP TABLE Users; --", FilterOperator.Equals, "x");

        var act = () => _sut.TranslateSingle(filter, Fields(Field("Email"), Field("Name")));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not declared*");
    }

    [Fact(DisplayName = "Field name with bracket-injection is bracket-quoted safely")]
    public void Translate_FieldNameWithBracketChar_IsSafelyQuoted()
    {
        // The column itself has a bracket that could break quoting — it's escaped
        var filter = FilterExpression.Leaf("Tricky]Field", FilterOperator.Equals, "v");

        // Without schema validation (empty schema), the name is just bracket-quoted
        var result = _sut.TranslateSingle(filter, []);  // Empty schema — no validation

        // The bracket in the column name should be escaped to ]]
        result.WhereClause.Should().Be("[Tricky]]Field] = @p0");
    }

    // -----------------------------------------------------------------------
    // TASK-016-07: Date field relative filter resolution
    // -----------------------------------------------------------------------

    [Theory(DisplayName = "Relative date keywords are resolved to concrete DateTime values")]
    [InlineData("today")]
    [InlineData("yesterday")]
    [InlineData("last30days")]
    [InlineData("last7days")]
    [InlineData("last90days")]
    [InlineData("last365days")]
    [InlineData("thisweek")]
    [InlineData("thismonth")]
    [InlineData("thisyear")]
    public void Translate_RelativeDateKeyword_ResolvesToDateTime(string keyword)
    {
        var filter = FilterExpression.Leaf("CreatedAt", FilterOperator.GreaterThan, keyword);
        var fields = Fields(Field("CreatedAt", "datetime"));

        var result = _sut.TranslateSingle(filter, fields);

        result.Parameters["@p0"].Should().BeOfType<DateTime>();
    }

    [Fact(DisplayName = "Unknown value on date field is passed through unchanged")]
    public void Translate_UnknownDateValue_PassedThrough()
    {
        var filter = FilterExpression.Leaf("CreatedAt", FilterOperator.GreaterThan, "2025-01-01");
        var fields = Fields(Field("CreatedAt", "datetime"));

        var result = _sut.TranslateSingle(filter, fields);

        // "2025-01-01" is not a relative keyword, so it's passed through as-is
        result.Parameters["@p0"].Should().Be("2025-01-01");
    }

    // -----------------------------------------------------------------------
    // TASK-016-07: JSON serialization round-trip
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FilterExpression serializes to JSON and deserializes back correctly")]
    public void FilterExpression_JsonRoundTrip_PreservesStructure()
    {
        var original = FilterExpression.And(
            FilterExpression.Leaf("Age", FilterOperator.GreaterThan, 18),
            FilterExpression.Or(
                FilterExpression.Leaf("Status", FilterOperator.Equals, "active"),
                FilterExpression.Leaf("Status", FilterOperator.Equals, "trial")
            )
        );

        var json = original.ToJson();
        var restored = FilterExpression.FromJson(json);

        var composite = restored.Should().BeOfType<CompositeFilterExpression>().Subject;
        composite.LogicalOperator.Should().Be(LogicalOperator.And);
        composite.Children.Should().HaveCount(2);
        composite.Children[0].Should().BeOfType<LeafFilterExpression>()
            .Which.FieldName.Should().Be("Age");
        composite.Children[1].Should().BeOfType<CompositeFilterExpression>()
            .Which.LogicalOperator.Should().Be(LogicalOperator.Or);
    }

    [Fact(DisplayName = "LeafFilterExpression serializes correctly")]
    public void LeafExpression_JsonRoundTrip_PreservesFields()
    {
        var leaf = new LeafFilterExpression
        {
            FieldName = "Email",
            Operator = FilterOperator.Like,
            Value = "%@example.com"
        };

        var json = leaf.ToJson();
        var restored = FilterExpression.FromJson(json);

        var restoredLeaf = restored.Should().BeOfType<LeafFilterExpression>().Subject;
        restoredLeaf.FieldName.Should().Be("Email");
        restoredLeaf.Operator.Should().Be(FilterOperator.Like);
    }

    // -----------------------------------------------------------------------
    // FilterExpressionValidator tests
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Validator accepts valid leaf expression")]
    public void Validator_ValidLeaf_IsValid()
    {
        var validator = new FilterExpressionValidator();
        var filter = FilterExpression.Leaf("Email", FilterOperator.Equals, "a@b.com");

        var result = validator.ValidateSingle(filter, Fields(Field("Email")));

        result.IsValid.Should().BeTrue();
    }

    [Fact(DisplayName = "Validator rejects leaf with unknown field name")]
    public void Validator_UnknownField_ReturnsError()
    {
        var validator = new FilterExpressionValidator();
        var filter = FilterExpression.Leaf("NonExistent", FilterOperator.Equals, "v");

        var result = validator.ValidateSingle(filter, Fields(Field("Email")));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NonExistent"));
    }

    [Fact(DisplayName = "Validator rejects leaf with empty FieldName")]
    public void Validator_EmptyFieldName_ReturnsError()
    {
        var validator = new FilterExpressionValidator();
        var filter = FilterExpression.Leaf("", FilterOperator.Equals, "v");

        var result = validator.ValidateSingle(filter, []);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("FieldName"));
    }

    [Fact(DisplayName = "Validator rejects IN operator with null value")]
    public void Validator_InWithNullValue_ReturnsError()
    {
        var validator = new FilterExpressionValidator();
        var filter = FilterExpression.Leaf("Status", FilterOperator.In, null);

        var result = validator.ValidateSingle(filter, Fields(Field("Status")));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("IN"));
    }

    [Fact(DisplayName = "Validator rejects composite with no children")]
    public void Validator_CompositeWithNoChildren_ReturnsError()
    {
        var validator = new FilterExpressionValidator();
        var composite = new CompositeFilterExpression
        {
            LogicalOperator = LogicalOperator.And,
            Children = []
        };

        var result = validator.ValidateSingle(composite, Fields(Field("Email")));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least one child"));
    }

    [Fact(DisplayName = "Validator rejects non-filterable field")]
    public void Validator_NonFilterableField_ReturnsError()
    {
        var validator = new FilterExpressionValidator();
        var filter = FilterExpression.Leaf("BinaryData", FilterOperator.Equals, "x");

        var fields = Fields(
            Field("Email"),
            new FieldDefinitionDto { FieldName = "BinaryData", FieldType = "varbinary", IsFilterable = false }
        );

        var result = validator.ValidateSingle(filter, fields);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("BinaryData"));
    }

    [Fact(DisplayName = "Validator accepts valid complex AND/OR expression")]
    public void Validator_ValidComplexExpression_IsValid()
    {
        var validator = new FilterExpressionValidator();
        var filter = FilterExpression.And(
            FilterExpression.Leaf("Age", FilterOperator.GreaterThan, 18),
            FilterExpression.Or(
                FilterExpression.Leaf("Status", FilterOperator.Equals, "active"),
                FilterExpression.Leaf("Status", FilterOperator.Equals, "trial")
            )
        );

        var fields = Fields(Field("Age", "int"), Field("Status"));
        var result = validator.ValidateSingle(filter, fields);

        result.IsValid.Should().BeTrue();
    }

    [Fact(DisplayName = "Validator rejects invalid date value on date field")]
    public void Validator_InvalidDateValue_ReturnsError()
    {
        var validator = new FilterExpressionValidator();
        var filter = FilterExpression.Leaf("CreatedAt", FilterOperator.GreaterThan, "not-a-date");

        var result = validator.ValidateSingle(filter, Fields(Field("CreatedAt", "datetime")));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not-a-date") || e.Contains("date"));
    }
}
