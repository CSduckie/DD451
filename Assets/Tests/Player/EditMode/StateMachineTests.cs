using System.Collections.Generic;
using NUnit.Framework;

public class StateMachineTests
{
    [Test]
    public void FirstEnterAndSameStateRequestHaveExactCounts()
    {
        RecordingOwner owner = new RecordingOwner();
        StateMachine machine = new StateMachine();
        machine.Init(owner);

        Assert.That(machine.ChangeState<FirstState>(), Is.True);
        Assert.That(machine.ChangeState<FirstState>(), Is.False);
        Assert.That(machine.hasState, Is.True);
        Assert.That(machine.CurrentStateType, Is.EqualTo(typeof(FirstState)));
        Assert.That(owner.calls, Is.EqualTo(new[] { "First.Init", "First.Enter" }));
        machine.Stop();
    }

    [Test]
    public void OldExitHappensBeforeTargetCreationAndCachedStatesAreReused()
    {
        RecordingOwner owner = new RecordingOwner();
        StateMachine machine = new StateMachine();
        machine.Init(owner);
        machine.ChangeState<FirstState>();
        StateBase first = machine.CurrentState;
        owner.calls.Clear();

        Assert.That(machine.ChangeState<SecondState>(), Is.True);
        Assert.That(owner.calls, Is.EqualTo(new[] { "First.Exit", "Second.Init", "Second.Enter" }));
        owner.calls.Clear();
        Assert.That(machine.ChangeState<FirstState>(), Is.True);
        Assert.That(machine.CurrentState, Is.SameAs(first));
        Assert.That(owner.calls, Is.EqualTo(new[] { "Second.Exit", "First.Enter" }));
        machine.Stop();
    }

    [Test]
    public void ExplicitReentryDoesNotCreateOrInitializeAnotherState()
    {
        RecordingOwner owner = new RecordingOwner();
        StateMachine machine = new StateMachine();
        machine.Init(owner);
        machine.ChangeState<FirstState>();
        StateBase state = machine.CurrentState;
        owner.calls.Clear();

        Assert.That(machine.ChangeState<FirstState>(true), Is.True);
        Assert.That(machine.CurrentState, Is.SameAs(state));
        Assert.That(owner.calls, Is.EqualTo(new[] { "First.Exit", "First.Enter" }));
        machine.Stop();
    }

    [Test]
    public void StopBeforeStartOrRepeatedAfterUseExitsAndUninitializesOnlyOnce()
    {
        RecordingOwner owner = new RecordingOwner();
        StateMachine machine = new StateMachine();
        Assert.DoesNotThrow(() => machine.Stop());
        machine.Init(owner);
        machine.ChangeState<FirstState>();
        machine.ChangeState<SecondState>();
        owner.calls.Clear();

        machine.Stop();
        machine.Stop();
        Assert.That(machine.hasState, Is.False);
        Assert.That(machine.CurrentStateType, Is.Null);
        Assert.That(machine.CurrentState, Is.Null);
        Assert.That(owner.calls, Is.EquivalentTo(new[] { "Second.Exit", "First.UnInit", "Second.UnInit" }));
        Assert.That(owner.calls[0], Is.EqualTo("Second.Exit"));
        Assert.That(machine.ChangeState<FirstState>(), Is.False);

        machine.Init(owner);
        owner.calls.Clear();
        Assert.That(machine.ChangeState<FirstState>(), Is.True);
        Assert.That(owner.calls, Is.EqualTo(new[] { "First.Init", "First.Enter" }));
        machine.Stop();
    }

    [Test]
    public void SeparateCharactersHaveIndependentCachesAndOnlyExplicitUpdatesRun()
    {
        RecordingOwner firstOwner = new RecordingOwner();
        RecordingOwner secondOwner = new RecordingOwner();
        StateMachine firstMachine = new StateMachine();
        StateMachine secondMachine = new StateMachine();
        firstMachine.Init(firstOwner);
        secondMachine.Init(secondOwner);
        firstMachine.ChangeState<FirstState>();
        secondMachine.ChangeState<FirstState>();
        firstOwner.calls.Clear();
        secondOwner.calls.Clear();

        Assert.That(firstMachine.CurrentState, Is.Not.SameAs(secondMachine.CurrentState));
        firstMachine.CurrentState.Update();
        firstMachine.CurrentState.FixedUpdate();
        firstMachine.CurrentState.LateUpdate();
        Assert.That(firstOwner.calls, Is.EqualTo(new[] { "First.Update", "First.FixedUpdate", "First.LateUpdate" }));
        Assert.That(secondOwner.calls, Is.Empty);
        firstMachine.Stop();
        Assert.That(secondMachine.hasState, Is.True);
        secondMachine.Stop();
    }

    public class RecordingOwner : IStateMachineOwner
    {
        public readonly List<string> calls = new List<string>();
    }

    public abstract class RecordingState : StateBase
    {
        protected RecordingOwner owner;
        protected abstract string Label { get; }

        public override void Init(IStateMachineOwner owner)
        {
            this.owner = (RecordingOwner)owner;
            this.owner.calls.Add(Label + ".Init");
        }

        public override void Enter()
        {
            owner.calls.Add(Label + ".Enter");
        }

        public override void Exit()
        {
            owner.calls.Add(Label + ".Exit");
        }

        public override void UnInit()
        {
            owner.calls.Add(Label + ".UnInit");
        }

        public override void Update()
        {
            owner.calls.Add(Label + ".Update");
        }

        public override void FixedUpdate()
        {
            owner.calls.Add(Label + ".FixedUpdate");
        }

        public override void LateUpdate()
        {
            owner.calls.Add(Label + ".LateUpdate");
        }
    }

    public class FirstState : RecordingState
    {
        protected override string Label { get { return "First"; } }
    }

    public class SecondState : RecordingState
    {
        protected override string Label { get { return "Second"; } }
    }
}
