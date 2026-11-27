// Excel-liknande tangentbordsnavigering mellan timceller (input.hc) inom en container.
// Varje rad med celler har klassen .g-row och data-alloc; varje cell data-col (0–11).

export function rowsIn(container) {
  return [...container.querySelectorAll('.g-row[data-alloc]')].filter((r) => r.querySelector('input.hc'))
}

export function cellAt(row, col) {
  return row?.querySelector(`input.hc[data-col="${col}"]`) ?? null
}

export function focusCell(input) {
  if (!input) return false
  input.focus({ preventScroll: false })
  input.select?.()
  input.scrollIntoView({ block: 'nearest', inline: 'nearest' })
  return true
}

/** Flytta fokus från `input` i riktning dr (rader) / dc (kolumner). */
export function move(container, input, dr, dc) {
  const row = input.closest('.g-row')
  const col = Number(input.dataset.col)
  if (dr) {
    const rows = rowsIn(container)
    const i = rows.indexOf(row)
    return focusCell(cellAt(rows[i + dr], col))
  }
  const target = col + dc
  if (target < 0 || target > 11) return false
  return focusCell(cellAt(row, target))
}

export function caretAtStart(input) {
  return input.selectionStart === 0 && input.selectionEnd === 0
}
export function caretAtEnd(input) {
  const len = input.value.length
  return input.selectionStart === len && input.selectionEnd === len
}

/** Returnerar matris av mål { allocId, col } för inklistring från startcellen. */
export function pasteTargets(container, input, matrix) {
  const rows = rowsIn(container)
  const start = rows.indexOf(input.closest('.g-row'))
  const col0 = Number(input.dataset.col)
  const out = []
  matrix.forEach((vals, r) => {
    const row = rows[start + r]
    if (!row) return
    vals.forEach((v, c) => {
      const col = col0 + c
      if (col > 11 || v === null || Number.isNaN(v)) return
      out.push({ allocId: Number(row.dataset.alloc), col, value: v, el: cellAt(row, col) })
    })
  })
  return out
}
