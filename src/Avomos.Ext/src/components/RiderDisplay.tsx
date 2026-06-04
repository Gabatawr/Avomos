import { useState } from 'react';
import type { MatchedRider } from '../lib/types';
import { api } from '../lib/api';

interface Props {
  riders: MatchedRider[];
  canCreate: boolean;
  loading: boolean;
  trackIds: string[];
  threshold: number;
  onRidersUpdate: (riders: MatchedRider[]) => void;
  onThresholdChange: (val: number) => void;
  onFlushRiders?: () => void;
  onChatMessage?: (text: string) => void;
}

export function RiderDisplay({ riders, canCreate, loading, trackIds, threshold, onRidersUpdate, onThresholdChange, onFlushRiders, onChatMessage }: Props) {
  const [saving, setSaving] = useState(false);
  const [deleting, setDeleting] = useState<string | null>(null);

  if (trackIds.length < 3) return null;

  const handleSave = async () => {
    setSaving(true);
    const result = await api.riderCreate(trackIds, threshold);
    if (result) {
      onChatMessage?.(result.message || `Rider ${result.status}`);
      if (result.status === 'created' || result.status === 'replaced') {
        onFlushRiders?.();
        const matchRes = await api.riderMatch(trackIds, threshold);
        if (matchRes) {
          onRidersUpdate(matchRes.riders);
        }
      }
    } else {
      onChatMessage?.('Error saving rider');
    }
    setSaving(false);
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this rider?')) return;
    setDeleting(id);
    const ok = await api.riderDelete(id);
    if (ok) {
      onChatMessage?.('Rider deleted');
      onFlushRiders?.();
      const matchRes = await api.riderMatch(trackIds, threshold);
      if (matchRes) {
        onRidersUpdate(matchRes.riders);
      }
    } else {
      onChatMessage?.('Error deleting rider');
    }
    setDeleting(null);
  };

  const handleThreshold = (e: React.ChangeEvent<HTMLInputElement>) => {
    const val = parseFloat(e.target.value);
    onThresholdChange(val);
  };

  const top = riders.slice(0, 3);

  return (
    <div className="ac-riders">
      <span className="ac-riders-names">
        {loading && <span className="ac-riders-loading">Loading...</span>}
        {!loading && riders.length > 0 && top.map((r, i) => (
          <span key={r.riderId} className={`ac-rider-chip${r.type === 'custom' ? ' ac-rider-custom' : ''}`} title={`${r.name}: ${Math.round(r.score * 100)}%`}>
            <span className="ac-rider-chip-name">{r.name}</span>
            <span className="ac-rider-chip-actions">
              <span className="ac-rider-chip-score">{Math.round(r.score * 100)}%</span>
              {r.type === 'custom' && (
                <span className="ac-rider-chip-del" onClick={() => handleDelete(r.riderId)}>
                  {deleting === r.riderId ? '…' : '×'}
                </span>
              )}
            </span>
            {i < top.length - 1 && <span className="ac-rider-chip-sep">·</span>}
          </span>
        ))}
      </span>
      <span className="ac-riders-threshold">
        <input
          type="range" className="ac-range" min="0" max="1" step="0.05"
          value={threshold}
          onChange={handleThreshold}
        />
        <span className="ac-riders-thr-val">{threshold.toFixed(2)}</span>
      </span>
      <button className="ac-riders-save-btn" onClick={handleSave} disabled={!canCreate || saving}>
        {saving ? '...' : '+ Rider'}
      </button>
    </div>
  );
}
