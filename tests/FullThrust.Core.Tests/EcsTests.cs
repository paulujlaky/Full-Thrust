using FullThrust.Core.Ecs;

namespace FullThrust.Core.Tests;

public class EcsTests {

    private struct A {

        public int Value;

    }

    private struct B {

        public double Value;

    }

    private struct C {

        public bool Flag;

    }

    [Fact]
    public void DefaultEntityIsNullAndNotAlive() {

        var world = new World();

        Assert.True(Entity.Null.IsNull);
        Assert.False(world.IsAlive(default));

    }

    [Fact]
    public void DestroyedEntityIsRecycledWithNewGeneration() {

        var world = new World();

        var first = world.Create();

        Assert.True(world.Destroy(first));
        Assert.False(world.IsAlive(first));
        Assert.False(world.Destroy(first));

        var second = world.Create();

        Assert.Equal(first.Index, second.Index);
        Assert.NotEqual(first.Generation, second.Generation);
        Assert.True(world.IsAlive(second));
        Assert.Equal(1, world.AliveCount);

    }

    [Fact]
    public void StaleHandleDoesNotSeeRecycledComponents() {

        var world = new World();

        var stale = world.Create();

        world.Destroy(stale);

        var fresh = world.Create();

        world.Add(fresh, new A { Value = 7 });

        Assert.False(world.Has<A>(stale));
        Assert.True(world.Has<A>(fresh));

    }

    [Fact]
    public void RemoveSwapsLastComponentIntoHole() {

        var world = new World();

        var e0 = world.Create();
        var e1 = world.Create();
        var e2 = world.Create();

        world.Add(e0, new A { Value = 0 });
        world.Add(e1, new A { Value = 1 });
        world.Add(e2, new A { Value = 2 });

        Assert.True(world.Remove<A>(e0));

        Assert.Equal(2, world.Store<A>().Count);
        Assert.Equal(1, world.Get<A>(e1).Value);
        Assert.Equal(2, world.Get<A>(e2).Value);
        Assert.False(world.Has<A>(e0));

    }

    [Fact]
    public void ReservedWorldAddsComponentsWithoutAllocating() {

        var world = new World();

        world.Store<A>();
        world.Reserve(256);

        var early = world.Create();

        world.Add(early, new A());
        world.Add(early, new B());

        for (var i = 0; i < 100; i++) {

            world.Create();

        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var late = world.Create();

        world.Add(late, new A { Value = 1 });
        world.Add(late, new B());

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1, world.Get<A>(late).Value);
        Assert.True(world.Capacity >= 256);

    }

    [Fact]
    public void GetReturnsMutableReference() {

        var world = new World();

        var entity = world.Create();

        world.Add(entity, new A { Value = 1 });
        world.Get<A>(entity).Value = 42;

        Assert.Equal(42, world.Get<A>(entity).Value);

    }

    [Fact]
    public void DestroyRemovesAllComponents() {

        var world = new World();

        var entity = world.Create();

        world.Add(entity, new A());
        world.Add(entity, new B());
        world.Destroy(entity);

        Assert.Equal(0, world.Store<A>().Count);
        Assert.Equal(0, world.Store<B>().Count);

    }

    [Fact]
    public void AddingDuplicateComponentThrows() {

        var world = new World();

        var entity = world.Create();

        world.Add(entity, new A());

        Assert.Throws<InvalidOperationException>(() => world.Add(entity, new A()));

    }

    [Fact]
    public void QueriesMatchOnlyEntitiesWithAllComponents() {

        var world = new World();

        var ab = world.Create();
        var a = world.Create();
        var abc = world.Create();

        world.Add(ab, new A { Value = 1 });
        world.Add(ab, new B { Value = 1 });
        world.Add(a, new A { Value = 2 });
        world.Add(abc, new A { Value = 3 });
        world.Add(abc, new B { Value = 3 });
        world.Add(abc, new C { Flag = true });

        var pairs = 0;
        var triples = 0;

        foreach (var row in world.Query<A, B>()) {

            Assert.Equal(row.C1.Value, (int)row.C2.Value);
            pairs++;

        }

        foreach (var row in world.Query<A, B, C>()) {

            Assert.Equal(abc, row.Entity);
            Assert.True(row.C3.Flag);
            triples++;

        }

        Assert.Equal(2, pairs);
        Assert.Equal(1, triples);

    }

    [Fact]
    public void QueryRowsWriteThrough() {

        var world = new World();

        for (var i = 0; i < 10; i++) {

            var entity = world.Create();

            world.Add(entity, new A { Value = i });

        }

        foreach (var row in world.Query<A>()) {

            row.C1.Value *= 2;

        }

        var sum = 0;

        foreach (var row in world.Query<A>()) {

            sum += row.C1.Value;

        }

        Assert.Equal(90, sum);

    }

    [Fact]
    public void StoresGrowBeyondInitialCapacity() {

        var world = new World();

        var entities = new Entity[1000];

        for (var i = 0; i < entities.Length; i++) {

            entities[i] = world.Create();
            world.Add(entities[i], new A { Value = i });

        }

        Assert.Equal(1000, world.AliveCount);
        Assert.Equal(999, world.Get<A>(entities[999]).Value);

    }

}
