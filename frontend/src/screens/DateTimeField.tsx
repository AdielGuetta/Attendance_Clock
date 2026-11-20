import { useEffect, useId, useRef, useState } from "react";
import { HOURS, joinDateTime, MINUTES, normalizeTime } from "../timeInput";

/**
 * Date + 24-hour time, replacing the browser's date-and-time box (which can show AM/PM, depending on the computer).
 * The date keeps the browser's calendar picker; the time is typed ("17:00") or picked from the 🕒 roller
 * (hours 00–23, minutes 00–59).
 *
 * `value` / `onChange` use the same "2026-11-18T17:00" format as before. onChange gets "" when both parts are empty
 * and null while the entry isn't a valid date + time yet, so the dialog can keep Save switched off.
 */
export default function DateTimeField({ label, value, onChange, required }: {
  label: string;
  value: string;
  onChange: (value: string | null) => void;
  required?: boolean;
}) {
  const id = useId();
  const [date, setDate] = useState(value.slice(0, 10));
  const [time, setTime] = useState(value.slice(11, 16));
  const [open, setOpen] = useState(false);
  const [upward, setUpward] = useState(false); // open the roller above the field when there's no room below
  const boxRef = useRef<HTMLDivElement>(null);

  const toggleRoller = () => {
    const rect = boxRef.current?.getBoundingClientRect();
    if (rect) setUpward(window.innerHeight - rect.bottom < 210 && rect.top > 210);
    setOpen((o) => !o);
  };

  const emit = (d: string, t: string) => {
    const joined = joinDateTime(d, t);
    onChange(joined === "" && required ? null : joined);
  };
  const changeDate = (d: string) => { setDate(d); emit(d, time); };
  const changeTime = (t: string) => { setTime(t); emit(date, t); };

  const normalized = normalizeTime(time);
  const [hour, minute] = normalized ? normalized.split(":") : ["", ""];
  const timeInvalid = time.trim() !== "" && normalized === null;

  // Close the roller on a click outside it or on Escape.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => { if (!boxRef.current?.contains(e.target as Node)) setOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") { e.stopPropagation(); setOpen(false); } };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey, true);
    return () => { document.removeEventListener("mousedown", onDown); document.removeEventListener("keydown", onKey, true); };
  }, [open]);

  // When the roller opens, scroll each column so the current hour / minute is in the middle.
  useEffect(() => {
    if (!open) return;
    boxRef.current?.querySelectorAll<HTMLElement>(".roller-col").forEach((col) => {
      const selected = col.querySelector<HTMLElement>(".selected");
      if (selected) col.scrollTop = selected.offsetTop - col.clientHeight / 2 + selected.clientHeight / 2;
    });
  }, [open]);

  return (
    <div className="dt-field">
      <span className="dt-label" id={`${id}-label`}>{label}</span>
      <div className="dt-inputs" role="group" aria-labelledby={`${id}-label`}>
        <input type="date" value={date} onChange={(e) => changeDate(e.target.value)} aria-label={`${label} date`} required={required} />
        <div className="time-box" ref={boxRef}>
          <input type="text" inputMode="numeric" placeholder="HH:MM" maxLength={5} value={time}
            aria-label={`${label} time (24-hour)`} aria-invalid={timeInvalid}
            onChange={(e) => changeTime(e.target.value)}
            onBlur={() => { if (normalized && normalized !== time) changeTime(normalized); }} />
          <button type="button" className="roller-toggle" aria-label={`Pick ${label} time`} aria-expanded={open}
            onClick={toggleRoller}>🕒</button>
          {open && (
            <div className={`roller ${upward ? "upward" : ""}`} role="dialog" aria-label={`${label} time`}>
              <ul className="roller-col" aria-label="Hours">
                {HOURS.map((h) => (
                  <li key={h}><button type="button" className={h === hour ? "selected" : ""}
                    onClick={() => changeTime(`${h}:${minute || "00"}`)}>{h}</button></li>
                ))}
              </ul>
              <ul className="roller-col" aria-label="Minutes">
                {MINUTES.map((m) => (
                  <li key={m}><button type="button" className={m === minute ? "selected" : ""}
                    onClick={() => { changeTime(`${hour || "00"}:${m}`); setOpen(false); }}>{m}</button></li>
                ))}
              </ul>
            </div>
          )}
        </div>
      </div>
      {timeInvalid && <span className="field-error">Use 24-hour time, like 17:00.</span>}
    </div>
  );
}
