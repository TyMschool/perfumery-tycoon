using System.Reflection;
using NetArchTest.Rules;
using Perfumery.Application.Abstractions.Messaging;
using Perfumery.Domain.Formulas;
using Perfumery.SharedKernel;

namespace Perfumery.ArchitectureTests;

public class ArchitectureTests
{
    private const string SharedKernelNamespace = "Perfumery.SharedKernel";
    private const string DomainNamespace = "Perfumery.Domain";
    private const string ApplicationNamespace = "Perfumery.Application";
    private const string InfrastructureNamespace = "Perfumery.Infrastructure";

    private static readonly Assembly SharedKernelAssembly = typeof(Result).Assembly;
    private static readonly Assembly DomainAssembly = typeof(Formula).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ICommand).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void SharedKernel_depends_on_no_other_project()
    {
        ShouldHold(Types.InAssembly(SharedKernelAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(DomainNamespace, ApplicationNamespace, InfrastructureNamespace)
            .GetResult());
        ReferencedProjects(SharedKernelAssembly).ShouldBeEmpty();
    }

    [Fact]
    public void Domain_depends_only_on_SharedKernel()
    {
        ShouldHold(Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace)
            .GetResult());
        ReferencedProjects(DomainAssembly).ShouldBe([SharedKernelNamespace]);
    }

    [Fact]
    public void Domain_has_no_EF_JSON_or_DI_references()
    {
        ShouldHold(Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "System.Text.Json",
                "Newtonsoft.Json",
                "Microsoft.Extensions",
                "FluentValidation")
            .GetResult());

        // Only the base class library and the shared kernel.
        DomainAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ShouldAllBe(name => name == SharedKernelNamespace || name.StartsWith("System."));
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure()
    {
        ShouldHold(Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult());
        ReferencedProjects(ApplicationAssembly).ShouldNotContain(InfrastructureNamespace);
    }

    [Fact]
    public void Infrastructure_implements_the_application_interfaces()
    {
        ReferencedProjects(InfrastructureAssembly).ShouldContain(ApplicationNamespace);

        var applicationInterfaces = ApplicationAssembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace!.StartsWith($"{ApplicationNamespace}.Abstractions"))
            .Where(type => !type.Namespace!.EndsWith(".Messaging"))
            .ToArray();

        applicationInterfaces.Length.ShouldBe(2);

        foreach (var contract in applicationInterfaces)
        {
            InfrastructureAssembly.GetTypes()
                .ShouldContain(type => type.IsClass && contract.IsAssignableFrom(type), $"{contract.Name} has no implementation");
        }
    }

    [Fact]
    public void Handlers_are_sealed_and_internal()
    {
        var handlers = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(IQueryHandler<,>));

        handlers.GetTypes().Count().ShouldBeGreaterThanOrEqualTo(7);
        ShouldHold(handlers.Should().BeSealed().And().NotBePublic().GetResult());
    }

    [Fact]
    public void Every_request_has_exactly_one_handler()
    {
        var types = ApplicationAssembly.GetTypes();
        var requests = types
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => ImplementsOpenGeneric(type, typeof(ICommand<>)) || ImplementsOpenGeneric(type, typeof(IQuery<>)))
            .ToArray();

        requests.Length.ShouldBe(7);
        foreach (var request in requests)
        {
            types.Count(type => type.Name == $"{request.Name}Handler").ShouldBe(1, $"{request.Name} needs one handler");
        }
    }

    [Fact]
    public void Domain_entities_have_no_public_setters_or_constructors()
    {
        var entities = DomainAssembly.GetTypes().Where(IsEntity).ToArray();

        entities.ShouldNotBeEmpty();
        foreach (var entity in entities)
        {
            entity.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{entity.Name}.{property.Name}")
                .ShouldBeEmpty();

            entity.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .ShouldBeEmpty($"{entity.Name} must be created through a factory method");
        }
    }

    [Fact]
    public void Domain_value_objects_and_entities_are_sealed()
    {
        ShouldHold(Types.InAssembly(DomainAssembly)
            .That()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .GetResult());
    }

    [Fact]
    public void Domain_events_are_sealed_and_named_as_such()
    {
        ShouldHold(Types.InAssembly(DomainAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .Should()
            .BeSealed()
            .And()
            .HaveNameEndingWith("DomainEvent")
            .GetResult());
    }

    private static bool IsEntity(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Entity<>))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsOpenGeneric(Type type, Type openInterface) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);

    private static string[] ReferencedProjects(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Perfumery."))
            .Order()];

    private static void ShouldHold(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful.ShouldBeTrue(
            $"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
}
