using Perfumery.SharedKernel;

namespace Perfumery.Domain.UnitTests.SharedKernel;

public class ResultTests
{
    private static readonly Error Boom = Error.Failure("Test.Boom", "It went wrong.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_carries_its_error()
    {
        Result result = Boom;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Boom);
    }

    [Fact]
    public void Value_converts_to_a_successful_result()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        Result<int> result = Boom;

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void A_failure_cannot_be_built_without_an_error()
    {
        Should.Throw<ArgumentException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void Entities_are_equal_by_type_and_id()
    {
        var a = new Thing(1);
        var sameId = new Thing(1);
        var otherId = new Thing(2);
        var otherType = new OtherThing(1);

        (a == sameId).ShouldBeTrue();
        a.GetHashCode().ShouldBe(sameId.GetHashCode());
        (a != otherId).ShouldBeTrue();
        a.Equals(otherType).ShouldBeFalse();
    }

    private sealed class Thing(int id) : Entity<int>(id);

    private sealed class OtherThing(int id) : Entity<int>(id);
}
