// Headless driver for FVU 1.0: construct the FVU object and call its validation
// method directly, then force exit so the AWT/Swing thread can't keep the JVM alive.
// args: <input.txt> <errOut.html-dir> <out.fvu> <fvuVersion=1.0> <samVer-int> <csi-or-empty>
public class RunFVU1 {
    public static void main(String[] a) {
        try {
            String input   = a[0];
            String errOut  = a[1];
            String fvuOut  = a[2];
            String version = a[3];           // "1.0"
            int    samVer  = Integer.parseInt(a[4].trim());
            String csi     = a.length > 5 ? a[5] : "";

            // Set CSI path if provided (mirrors main()).
            if (csi != null && csi.length() > 0) {
                com.tin.tds.c.a.pc(csi);
            }
            // Use the FULL (no-arg) ctor so all instance state the validation method
            // needs is initialized; never show the window, then validate and exit.
            com.tin.FVU.FVU fvu = new com.tin.FVU.FVU();
            try { fvu.setVisible(false); } catch (Throwable ignore) {}
            fvu.j(input, errOut, fvuOut, version, samVer, "");
            System.out.println("RUNFVU1_DONE");
        } catch (Throwable t) {
            t.printStackTrace();
        } finally {
            System.exit(0);   // kill any lingering AWT/Swing non-daemon thread
        }
    }
}
