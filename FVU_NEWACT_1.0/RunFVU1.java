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

            // Initialize the CSI-path static to a non-null value. FVU.j() calls
            // com.tin.tds.c.a.g().length() unconditionally, which NPEs if g() is null.
            // pc("") sets it to empty (no CSI); pc(csi) sets the real path.
            com.tin.tds.c.a.pc(csi == null ? "" : csi);
            com.tin.tds.c.a.b(0);   // mode flag the GUI sets (0 = no CSI verify)
            // Construct FVU with a real toolkit (JFrame needs it), then run validation
            // on a separate thread with a hard timeout — any modal JOptionPane or
            // network stall in the GUI-coupled validator can't block us forever.
            final com.tin.FVU.FVU fvu = new com.tin.FVU.FVU();
            try { fvu.setVisible(false); } catch (Throwable ignore) {}
            final String fi = input, fe = errOut, fo = fvuOut, fv = version;
            final int fs = samVer;
            Thread worker = new Thread(new Runnable() {
                public void run() {
                    try { fvu.j(fi, fe, fo, fv, fs, ""); System.out.println("RUNFVU1_DONE"); }
                    catch (Throwable t) { t.printStackTrace(); }
                }
            });
            worker.setDaemon(true);
            worker.start();
            worker.join(20000);   // 20s cap; validation of a tiny file is sub-second
        } catch (Throwable t) {
            t.printStackTrace();
        } finally {
            System.exit(0);   // kill any lingering AWT/Swing non-daemon thread
        }
    }
}
