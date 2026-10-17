namespace FullThrust.Game.Debug;

public static class TimeFormat {

    public static (int Days, int Hours, int Minutes, int Seconds) Split(double seconds) {

        var total = (long)Math.Floor(Math.Abs(seconds));

        return ((int)(total / 86400), (int)(total / 3600 % 24), (int)(total / 60 % 60), (int)(total % 60));

    }

}
