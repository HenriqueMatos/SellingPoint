using SellingPoint.App;

namespace SellingPoint.Tests;

/// <summary>
/// The single-till guard cannot be tested by launching two copies from here, but
/// the part of it that can silently stop working can: the name both copies have
/// to agree on.
/// </summary>
public class InstanceNameTests
{
    [Fact]
    public void Two_copies_started_the_same_way_agree_on_the_name()
    {
        // The trap this exists for: string.GetHashCode has been randomised per
        // process since .NET Core, so deriving the name from it would give the two
        // copies different names. Both would start, the guard would do nothing,
        // and nobody would find out until two tills fought over one database.
        Assert.Equal(Program.InstanceName([]), Program.InstanceName([]));
        Assert.Equal(
            Program.InstanceName(["--db=/tmp/festa.db"]),
            Program.InstanceName(["--db=/tmp/festa.db"]));
    }

    [Fact]
    public void A_scratch_database_opens_alongside_the_real_till()
    {
        // --db= exists to run against a throwaway database while the real one is
        // open - for development, and for walking someone through a problem on the
        // phone. Keying the guard on the database keeps that working.
        Assert.NotEqual(Program.InstanceName([]), Program.InstanceName(["--db=/tmp/rascunho.db"]));
        Assert.NotEqual(
            Program.InstanceName(["--db=/tmp/uma.db"]),
            Program.InstanceName(["--db=/tmp/outra.db"]));
    }

    [Fact]
    public void The_name_is_one_a_mutex_will_accept()
    {
        // Windows rejects a backslash in a mutex name, and a database path is full
        // of them. Hashing sidesteps that as well as the length limit.
        var name = Program.InstanceName([@"--db=C:\Users\Festa\AppData\Roaming\SellingPoint\sellingpoint.db"]);

        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain('/', name);
        Assert.True(name.Length < 60, name);
    }

    [Fact]
    public void Other_arguments_do_not_change_which_till_it_is()
    {
        // --tab= only chooses the screen to open on. Two copies differing by it are
        // still two copies of the same till.
        Assert.Equal(Program.InstanceName([]), Program.InstanceName(["--tab=2"]));
    }
}
