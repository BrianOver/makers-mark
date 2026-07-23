log="$CLAUDE_JOB_DIR/tmp/gpu_guard.log"; : > "$log"
for i in $(seq 1 45); do
  read u t < <(nvidia-smi --query-gpu=memory.used,temperature.gpu --format=csv,noheader,nounits | tr ',' ' ')
  rf=$(powershell -NoProfile -Command "[int]((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1024)" 2>/dev/null)
  echo "$(date +%H:%M:%S) vram_used_MB=$u temp=$t ram_free_MB=$rf" >> "$log"
  if [ "${t:-0}" -gt 83 ] || [ "${u:-0}" -gt 14000 ] || { [ -n "$rf" ] && [ "$rf" -lt 5000 ]; }; then
    echo "$(date +%H:%M:%S) BREACH -> interrupting ComfyUI" >> "$log"
    curl -s -X POST http://127.0.0.1:8188/interrupt >/dev/null 2>&1
    break
  fi
  sleep 60
done
echo "guard done" >> "$log"
