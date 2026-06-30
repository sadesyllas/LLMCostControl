using LLMCostControl.Domain.Budgets;
using LLMCostControl.Infrastructure.Repositories;

namespace LLMCostControl.Infrastructure.Tests;

public class GroupRepositoryTests : RepositoryTestBase
{
    [Fact]
    public async Task Add_then_GetById_returns_the_group()
    {
        var group = Group.Create("Engineers");

        var repo = new GroupRepository(Db);
        await repo.AddAsync(group);

        var fetched = await repo.GetByIdAsync(group.Id);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Engineers");
    }

    [Fact]
    public async Task GetAll_returns_all_groups()
    {
        var repo = new GroupRepository(Db);
        await repo.AddAsync(Group.Create("Alpha"));
        await repo.AddAsync(Group.Create("Beta"));

        var all = await repo.GetAllAsync();

        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task Delete_removes_the_group()
    {
        var group = Group.Create("ToDelete");
        var repo = new GroupRepository(Db);
        await repo.AddAsync(group);

        await repo.DeleteAsync(group.Id);

        var fetched = await repo.GetByIdAsync(group.Id);
        fetched.Should().BeNull();
    }

    [Fact]
    public async Task Update_changes_the_name()
    {
        var group = Group.Create("OldName");
        var repo = new GroupRepository(Db);
        await repo.AddAsync(group);

        group.Name = "NewName";
        await repo.UpdateAsync(group);

        var fetched = await repo.GetByIdAsync(group.Id);
        fetched!.Name.Should().Be("NewName");
    }
}
